using FinancialTransactionService.Domain.Entities;
using FinancialTransactionService.Domain.Enums;
using FinancialTransactionService.Domain.Exceptions;
using FinancialTransactionService.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FinancialTransactionService.Application.Transactions.Commands.ProcessTransaction;

/// <summary>
/// Handler do caso de uso "Processar Transação Financeira".
///
/// Responsabilidade central: executar a operação financeira real, 
/// movimentar saldos entre contas e transicionar o estado da transação, 
/// dentro de uma transação de banco de dados explícita e atômica.
///
/// Fluxo de execução:
///   1- Carrega Transaction do banco e inicia processamento (state machine)
///   2- Carrega contas envolvidas (anti-N+1, uma query)
///   3- Abre transação de banco de dados explícita (atomicidade)
///   4- Executa operação financeira conforme o tipo (debit/credit/transfer)
///   5- Conclui (Complete) ou falha (Fail) a transação de domínio
///   6- Persiste todas as mudanças e faz commit
///   7- Em caso de exceção: rollback + marca transação como Failed
/// </summary>
public sealed class ProcessTransactionCommandHandler
    : IRequestHandler<ProcessTransactionCommand, Unit>
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly IAccountRepository _accountRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ProcessTransactionCommandHandler> _logger;

    public ProcessTransactionCommandHandler(
        ITransactionRepository transactionRepository,
        IAccountRepository accountRepository,
        IUnitOfWork unitOfWork,
        ILogger<ProcessTransactionCommandHandler> logger)
    {
        _transactionRepository = transactionRepository;
        _accountRepository     = accountRepository;
        _unitOfWork            = unitOfWork;
        _logger                = logger;
    }

    public async Task<Unit> Handle(
        ProcessTransactionCommand request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Iniciando processamento de transação. " +
            "TransactionId: {TransactionId}. CorrelationId: {CorrelationId}.",
            request.TransactionId,
            request.CorrelationId);

        // ──────────────────────
        // 1- CARGA DA TRANSAÇÃO
        // ──────────────────────
        var transaction = await _transactionRepository
            .GetByIdAsync(request.TransactionId, cancellationToken);

        if (transaction is null)
        {
            // Se a transação não existe no banco, algo muito errado
            // aconteceu (inconsistência entre fila e banco de dados).
            // Logamos como Critical: requer investigação imediata pela equipe.
            // Não lançamos exceção para evitar loop infinito de retry no broker:
            // a mensagem seria rejeitada e iria para a DLQ para análise manual.
            _logger.LogCritical(
                "Transação não encontrada no banco de dados. " +
                "TransactionId: {TransactionId}. " +
                "Possível inconsistência entre RabbitMQ e PostgreSQL.",
                request.TransactionId);

            return Unit.Value;
        }

        // ─────────────────────────────────────────────────
        // GUARDA DE ESTADO: evita reprocessamento acidental
        // ─────────────────────────────────────────────────
        // Se a transação já está em estado terminal (Completed,
        // Failed, Cancelled), significa que a mensagem foi entregue
        // mais de uma vez pelo RabbitMQ (at-least-once delivery).
        // Retornamos silenciosamente — o processamento já aconteceu.
        if (transaction.IsTerminal)
        {
            _logger.LogWarning(
                "Transação já está em estado terminal, ignorando mensagem duplicada. " +
                "TransactionId: {TransactionId}. Status: {Status}.",
                transaction.Id,
                transaction.Status);

            return Unit.Value;
        }

        // Transição de estado: Pending -> Processing
        // Aqui a máquina de estados da entidade é acionada.
        transaction.StartProcessing();

        // ──────────────────────────────
        // 2- CARGA DE CONTAS — ANTI-N+1
        // ──────────────────────────────
        var accountIdsToLoad = new List<Guid>();

        if (transaction.SourceAccountId.HasValue)
            accountIdsToLoad.Add(transaction.SourceAccountId.Value);

        if (transaction.DestinationAccountId.HasValue)
            accountIdsToLoad.Add(transaction.DestinationAccountId.Value);

        var accounts = accountIdsToLoad.Count > 0
            ? await _accountRepository.GetByIdsAsync(accountIdsToLoad, cancellationToken)
            : new Dictionary<Guid, Account>();

        // ─────────────────────────────────────────────────────────────
        // 3- TRANSAÇÃO DE BANCO DE DADOS EXPLÍCITA
        // Garante atomicidade: todos os UPDATEs persistem juntos ou
        // nenhum persiste. Fundamental para integridade financeira.
        // ─────────────────────────────────────────────────────────────
        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            // ─────────────────────────────────────────────────────────
            // 4- EXECUÇÃO DA OPERAÇÃO FINANCEIRA
            // Cada tipo de transação tem um perfil diferente de
            // movimentação de saldo entre contas.
            // ─────────────────────────────────────────────────────────
            ExecuteFinancialOperation(transaction, accounts);

            // ─────────────────────────────────────────────────────────
            // 5- CONCLUSÃO DA TRANSAÇÃO DE DOMÍNIO
            // Transition: Processing → Completed
            // Este método também registra o TransactionCompletedEvent.
            // ─────────────────────────────────────────────────────────
            transaction.Complete();

            _logger.LogInformation(
                "Operação financeira executada. Persistindo. " +
                "TransactionId: {TransactionId}. Tipo: {Type}. " +
                "Valor: {AmountInCents} centavos.",
                transaction.Id,
                transaction.Type,
                transaction.AmountInCents);

            // ─────────────────────────────────────────────────────────
            // 6- PERSISTÊNCIA + COMMIT
            // SaveChangesAsync persiste Transaction (status=Completed)
            // + Account(s) (novos saldos) em uma única operação.
            // O UoW também despacha os DomainEvents (Cap. 4).
            // ─────────────────────────────────────────────────────────
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            _logger.LogInformation(
                "Transação processada com sucesso. " +
                "TransactionId: {TransactionId}. CorrelationId: {CorrelationId}.",
                transaction.Id,
                request.CorrelationId);
        }
        catch (Exception ex)
        {
            // ─────────────────────────────────────────────────────────
            // 7- TRATAMENTO DE FALHA — ROLLBACK + ESTADO FAILED
            // ─────────────────────────────────────────────────────────
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);

            _logger.LogError(
                ex,
                "Falha no processamento da transação. " +
                "TransactionId: {TransactionId}. CorrelationId: {CorrelationId}. " +
                "Motivo: {ErrorMessage}.",
                transaction.Id,
                request.CorrelationId,
                ex.Message);

            // Após o rollback, precisamos de uma NOVA transação de
            // banco de dados para persistir o status Failed — a anterior
            // foi revertida e está encerrada. Por isso o BeginTransaction/Commit
            // aninhado abaixo. É um padrão necessário em cenários de compensação.
            await PersistFailureAsync(transaction, ex.Message, cancellationToken);

            // Re-lançamos a exceção para que o consumer do RabbitMQ
            // saiba que o processamento falhou e possa executar o nack()
            // na mensagem, enviando-a para a DLQ conforme configurado.
            throw;
        }

        return Unit.Value;
    }

    // ─────────────────────────────────────────
    // OPERAÇÃO FINANCEIRA — ROTEAMENTO POR TIPO
    // ──────────────────────────────────────────
    /// <summary>
    /// Roteia a execução financeira para o método correto conforme o tipo
    /// da transação e executa os débitos/créditos nas entidades Account.
    /// </summary>
    private void ExecuteFinancialOperation(
        Transaction transaction,
        Dictionary<Guid, Account> accounts)
    {
        switch (transaction.Type)
        {
            case TransactionType.Transfer:
                ExecuteTransfer(transaction, accounts);
                break;

            case TransactionType.Debit:
            case TransactionType.Payment:
                ExecuteDebit(transaction, accounts);
                break;

            case TransactionType.Credit:
            case TransactionType.Refund:
                ExecuteCredit(transaction, accounts);
                break;

            default:
                throw new InvalidOperationException(
                    $"Tipo de transação não suportado para processamento: {transaction.Type}.");
        }
    }

    /// <summary>
    /// Executa uma transferência: debita a conta de origem e credita a de destino.
    /// Ambas as operações ocorrem na mesma transação de banco de dados.
    /// </summary>
    private void ExecuteTransfer(
        Transaction transaction,
        Dictionary<Guid, Account> accounts)
    {
        var sourceAccount = GetAccountOrThrow(
            accounts,
            transaction.SourceAccountId!.Value,
            "origem");

        var destinationAccount = GetAccountOrThrow(
            accounts,
            transaction.DestinationAccountId!.Value,
            "destino");

        // A ordem importa aqui — debitamos primeiro para
        // detectar InsufficientFundsException antes de creditar.

        // O método Debit() da entidade Account lança a exceção com
        // dados ricos (accountId, disponível, solicitado) se o saldo
        // for insuficiente. O catch() do handler fará rollback.
        sourceAccount.Debit(transaction.AmountInCents);
        destinationAccount.Credit(transaction.AmountInCents);

        _logger.LogDebug(
            "Transferência executada. " +
            "Origem: {SourceAccountId} (-{Amount} centavos). " +
            "Destino: {DestinationAccountId} (+{Amount} centavos).",
            sourceAccount.Id,
            transaction.AmountInCents,
            destinationAccount.Id,
            transaction.AmountInCents);
    }

    /// <summary>
    /// Executa um débito ou pagamento: retira saldo da conta de origem.
    /// </summary>
    private void ExecuteDebit(
        Transaction transaction,
        Dictionary<Guid, Account> accounts)
    {
        var sourceAccount = GetAccountOrThrow(
            accounts,
            transaction.SourceAccountId!.Value,
            "origem");

        sourceAccount.Debit(transaction.AmountInCents);

        _logger.LogDebug(
            "Débito executado. Conta: {AccountId}. " +
            "Valor: -{AmountInCents} centavos.",
            sourceAccount.Id,
            transaction.AmountInCents);
    }

    /// <summary>
    /// Executa um crédito ou reembolso: adiciona saldo à conta de destino.
    /// </summary>
    private void ExecuteCredit(
        Transaction transaction,
        Dictionary<Guid, Account> accounts)
    {
        var destinationAccount = GetAccountOrThrow(
            accounts,
            transaction.DestinationAccountId!.Value,
            "destino");

        destinationAccount.Credit(transaction.AmountInCents);

        _logger.LogDebug(
            "Crédito executado. Conta: {AccountId}. " +
            "Valor: +{AmountInCents} centavos.",
            destinationAccount.Id,
            transaction.AmountInCents);
    }

    // ─────────────────
    // HELPERS PRIVADOS
    // ─────────────────

    /// <summary>
    /// Recupera uma conta do dicionário ou lança AccountNotFoundException.
    /// Centraliza a verificação de existência evitando repetição nos métodos
    /// de operação financeira.
    /// </summary>
    private static Account GetAccountOrThrow(
        Dictionary<Guid, Account> accounts,
        Guid accountId,
        string role)
    {
        if (!accounts.TryGetValue(accountId, out var account))
            throw new AccountNotFoundException(accountId);

        // Conta inativa durante processamento é mais grave que durante
        // criação: a transação já foi criada e persistida. Neste ponto,
        // a conta foi desativada entre a criação e o processamento.
        // O Fail() registrará este motivo para auditoria e suporte.
        if (!account.IsActive)
            throw new InvalidOperationException(
                $"Conta de {role} '{accountId}' está inativa. " +
                "A transação não pode ser processada.");

        return account;
    }

    /// <summary>
    /// Persiste o estado Failed da transação após um rollback.
    /// Opera em uma nova transação de banco de dados, pois a original
    /// foi revertida no bloco catch do fluxo principal.
    /// </summary>
    private async Task PersistFailureAsync(
        Transaction transaction,
        string failureReason,
        CancellationToken cancellationToken)
    {
        try
        {
            // Transition: Processing → Failed
            // O método Fail() registra o TransactionFailedEvent internamente,
            // que será despachado pelo UoW no SaveChangesAsync abaixo.
            transaction.Fail(failureReason);

            await _unitOfWork.BeginTransactionAsync(cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            _logger.LogInformation(
                "Status Failed persistido com sucesso. " +
                "TransactionId: {TransactionId}.",
                transaction.Id);
        }
        catch (Exception persistEx)
        {
            // Se até a persistência do status Failed falhar,
            // estamos em um cenário de falha catastrófica de infraestrutura.
            // A transação ficará presa em status Processing no banco.
            // Um job de reconciliação (fora do escopo deste portfólio)
            // seria responsável por detectar e corrigir este estado.
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);

            _logger.LogCritical(
                persistEx,
                "FALHA CRÍTICA: não foi possível persistir status Failed. " +
                "TransactionId: {TransactionId}. " +
                "A transação está presa em status Processing. " +
                "Investigação manual necessária.",
                transaction.Id);
        }
    }
}