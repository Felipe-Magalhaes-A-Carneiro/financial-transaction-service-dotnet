using System.Text.Json;
using FinancialTransactionService.Application.Common.Interfaces;
using FinancialTransactionService.Application.Transactions.DTOs;
using FinancialTransactionService.Application.Transactions.Messages;
using FinancialTransactionService.Domain.Entities;
using FinancialTransactionService.Domain.Exceptions;
using FinancialTransactionService.Domain.Interfaces;
using FinancialTransactionService.Domain.ValueObjects;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FinancialTransactionService.Application.Transactions.Commands.CreateTransaction;

/// <summary>
/// Handler do caso de uso "Criar Transação Financeira".
///
/// Orquestra o fluxo completo: idempotência → validação de negócio
/// → criação de entidade → persistência → publicação assíncrona.
///
/// Segue o princípio Single Responsibility: este handler conhece
/// O QUE fazer (o caso de uso), não COMO as dependências funcionam
/// internamente (Redis, RabbitMQ, EF Core são todos atrás de interfaces).
/// </summary>
public sealed class CreateTransactionCommandHandler
    : IRequestHandler<CreateTransactionCommand, TransactionDto>
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly IAccountRepository _accountRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IIdempotencyService _idempotencyService;
    private readonly IMessagePublisher _messagePublisher;
    private readonly ILogger<CreateTransactionCommandHandler> _logger;

    // A chave de idempotência usa um prefixo "txn:create:" para
    // isolar as chaves deste caso de uso das de outros no Redis.
    // Isso evita colisões se o mesmo CorrelationId for usado em
    // diferentes endpoints (improvável mas defensivo).
    private const string IdempotencyKeyPrefix = "txn:create:";
    private const string TransactionRoutingKey = "transactions.created";

    public CreateTransactionCommandHandler(
        ITransactionRepository transactionRepository,
        IAccountRepository accountRepository,
        IUnitOfWork unitOfWork,
        IIdempotencyService idempotencyService,
        IMessagePublisher messagePublisher,
        ILogger<CreateTransactionCommandHandler> logger)
    {
        _transactionRepository = transactionRepository;
        _accountRepository     = accountRepository;
        _unitOfWork            = unitOfWork;
        _idempotencyService    = idempotencyService;
        _messagePublisher      = messagePublisher;
        _logger                = logger;
    }

    public async Task<TransactionDto> Handle(
        CreateTransactionCommand request,
        CancellationToken cancellationToken)
    {
        var idempotencyKey = $"{IdempotencyKeyPrefix}{request.CorrelationId}";

        // ─────────────────────────────────────────────────────────────
        // 1- VERIFICAÇÃO DE IDEMPOTÊNCIA
        // Antes de qualquer processamento, verificamos se este
        // CorrelationId já foi processado com sucesso.
        // ─────────────────────────────────────────────────────────────
        var cachedResponse = await _idempotencyService
            .GetAsync(idempotencyKey, cancellationToken);

        if (cachedResponse is not null)
        {
            _logger.LogInformation(
                "Requisição duplicada detectada. CorrelationId: {CorrelationId}. " +
                "Retornando resposta cacheada.",
                request.CorrelationId);

            // Desserializamos e retornamos exatamente a mesma resposta
            // que foi retornada na requisição original. Para o cliente,
            // é indistinguível de um processamento real — mas sem custo.
            return JsonSerializer.Deserialize<TransactionDto>(cachedResponse)!;
        }

        // ─────────────────────────────────────────────────────────────
        // 2- CONVERSÃO E VALIDAÇÃO DE VALOR (Money Value Object)
        // Aproveitamos todas as guards do Value Object para validar
        // valor e moeda antes de qualquer operação de I/O.
        // ─────────────────────────────────────────────────────────────
        var money = new Money(request.Amount, request.Currency);
        var amountInCents = money.ToAmountInCents();

        _logger.LogInformation(
            "Iniciando criação de transação. CorrelationId: {CorrelationId}. " +
            "Valor: {Amount}. Tipo: {Type}.",
            request.CorrelationId,
            money.ToFormattedString(),
            request.Type);

        // ─────────────────────────────────────────────────────────────
        // 3- CARGA DE CONTAS — ANTI-N+1
        // Coletamos todos os IDs necessários e fazemos UMA única query
        // SQL com WHERE Id IN (@ids), em vez de queries sequenciais.
        // ─────────────────────────────────────────────────────────────
        var accountIdsToLoad = new List<Guid>();

        if (request.SourceAccountId.HasValue)
            accountIdsToLoad.Add(request.SourceAccountId.Value);

        if (request.DestinationAccountId.HasValue)
            accountIdsToLoad.Add(request.DestinationAccountId.Value);

        // GetByIdsAsync retorna Dictionary<Guid, Account> para
        // acesso O(1) por chave nos passos seguintes — sem LINQ adicional.
        var accounts = accountIdsToLoad.Count > 0
            ? await _accountRepository.GetByIdsAsync(accountIdsToLoad, cancellationToken)
            : new Dictionary<Guid, Account>();

        // ─────────────────────────────────────────────────────────────
        // 4- VALIDAÇÃO DE EXISTÊNCIA DAS CONTAS
        // ─────────────────────────────────────────────────────────────
        if (request.SourceAccountId.HasValue
            && !accounts.ContainsKey(request.SourceAccountId.Value))
        {
            throw new AccountNotFoundException(request.SourceAccountId.Value);
        }

        if (request.DestinationAccountId.HasValue
            && !accounts.ContainsKey(request.DestinationAccountId.Value))
        {
            throw new AccountNotFoundException(request.DestinationAccountId.Value);
        }

        // ─────────────────────────────────────────────────────────────
        // 5- VALIDAÇÃO DE SALDO (REGRA DE NEGÓCIO)
        // Verificada aqui — antes da criação da entidade — para
        // lançar InsufficientFundsException com dados ricos para o log.
        // ─────────────────────────────────────────────────────────────
        if (request.SourceAccountId.HasValue
            && accounts.TryGetValue(request.SourceAccountId.Value, out var sourceAccount))
        {
            if (sourceAccount.BalanceInCents < amountInCents)
            {
                _logger.LogWarning(
                    "Saldo insuficiente para transação. Conta: {AccountId}. " +
                    "Disponível: {Available} centavos. Solicitado: {Requested} centavos.",
                    sourceAccount.Id,
                    sourceAccount.BalanceInCents,
                    amountInCents);

                throw new InsufficientFundsException(
                    accountId:                 sourceAccount.Id,
                    availableBalanceInCents:   sourceAccount.BalanceInCents,
                    requestedAmountInCents:    amountInCents);
            }
        }

        // ─────────────────────────────────────────────────────────────
        // 6- CRIAÇÃO DA ENTIDADE DE DOMÍNIO
        // O construtor de Transaction aplica todas as invariantes do
        // domínio e registra o TransactionCreatedEvent internamente.
        // ─────────────────────────────────────────────────────────────
        var transaction = new Transaction(
            sourceAccountId:      request.SourceAccountId,
            destinationAccountId: request.DestinationAccountId,
            amountInCents:        amountInCents,
            currency:             money.Currency,
            type:                 request.Type,
            correlationId:        request.CorrelationId,
            description:          request.Description);

        // ─────────────────────────────────────────────────────────────
        // 7- PERSISTÊNCIA
        // AddAsync rastreia a entidade no EF Core Change Tracker.
        // SaveChangesAsync efetiva o INSERT e despacha os DomainEvents
        // registrados na entidade (ainda será implementado).
        // ─────────────────────────────────────────────────────────────
        await _transactionRepository.AddAsync(transaction, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Transação persistida com sucesso. TransactionId: {TransactionId}. " +
            "CorrelationId: {CorrelationId}.",
            transaction.Id,
            transaction.CorrelationId);

        // ─────────────────────────────────────────────────────────────
        // 8- PUBLICAÇÃO ASSÍNCRONA NO BROKER
        // A transação foi salva — agora enfileiramos para processamento.
        // Os headers propagam o CorrelationId para rastreabilidade
        // fim-a-fim entre publisher e consumer.
        // ─────────────────────────────────────────────────────────────
        var message = new TransactionCreatedMessage(
            MessageId:            Guid.NewGuid(),
            TransactionId:        transaction.Id,
            SourceAccountId:      transaction.SourceAccountId,
            DestinationAccountId: transaction.DestinationAccountId,
            AmountInCents:        transaction.AmountInCents,
            Currency:             transaction.Currency,
            Type:                 transaction.Type,
            CorrelationId:        transaction.CorrelationId,
            OccurredOn:           transaction.CreatedAt);

        await _messagePublisher.PublishAsync(
            message:    message,
            routingKey: TransactionRoutingKey,
            headers: new Dictionary<string, object>
            {
                // Estes headers ficam visíveis na UI de gerenciamento
                // do RabbitMQ (http://localhost:15672) e são usados pelo
                // consumer para logging estruturado e distributed tracing.
                ["correlationId"] = transaction.CorrelationId.ToString(),
                ["transactionId"] = transaction.Id.ToString(),
                ["timestamp"]     = transaction.CreatedAt.ToString("O"),
                ["messageType"]   = nameof(TransactionCreatedMessage)
            },
            cancellationToken: cancellationToken);

        // ─────────────────────────────────────────────────────────────
        // 9- REGISTRO DE IDEMPOTÊNCIA
        // Registrado APÓS persistência e publicação bem-sucedidas.
        // TTL de 24h: retries dentro desta janela recebem resposta cacheada.
        // ─────────────────────────────────────────────────────────────
        var dto = TransactionDto.FromTransaction(transaction);

        await _idempotencyService.SetAsync(
            key:        idempotencyKey,
            value:      JsonSerializer.Serialize(dto),
            expiration: TimeSpan.FromHours(24),
            cancellationToken);

        _logger.LogInformation(
            "Transação criada e enfileirada. TransactionId: {TransactionId}. " +
            "Status: {Status}.",
            transaction.Id,
            transaction.Status);

        return dto;
    }
}