using FinancialTransactionService.Domain.Common;
using FinancialTransactionService.Domain.Enums;
using FinancialTransactionService.Domain.Events;

namespace FinancialTransactionService.Domain.Entities;

///<sumary>
/// Entidade central do domínio. Representa uma operação financeira com ciclo de vida completo e rastreável.
/// 
/// Invariantes garantidas por esta entidade:
/// - Uma transação nunca pode ter valor zero ou negativo.
/// - Transições de estado seguem a máquina de estados definida em TransactionStatus.
/// - Um estado terminal (Completed, Failed, Cancelled) nunca pode ser revertido.
/// - Toda falha registra obrigatoriamente um motivo auditável. 
///</sumary>


public sealed class Transaction : BaseEntity
{
    ///<sumary>
    /// ID da conta de origem. Obrigatório para Debit, Transfer e Payment.
    /// </summary>
    public Guid? SourceAccountId {get; private set; }

    ///<sumary>
    /// ID da conta de destino. Obrigatório para Credit, Transfer e Refund.
    /// </summary>
    public Guid? DestinationAccountId {get; private set; }

    ///<sumary>
    /// Valor da transação em centavos de BRL.
    /// Armazenado como long para precisão absoluta em cálculos financeiros.
    /// </summary>
    public long AmountInCents {get; private set; }

    /// <summary>
    /// Código ISO 4217 da moeda da transação (ex: "BRL", "USD").
    /// Armazenado explicitamente para garantir que o histórico de transações
    /// preserve a moeda original, independente de mudanças de configuração.
    /// </summary>
    public string Currency { get; private set; }

    ///<sumary>
    /// Valor da transação em centavos de BRL.
    /// Armazenado como long para precisão absoluta em cálculos financeiros.
    /// </summary>
    public TransactionType Type {get; private set; }

    /// <summary>Estado atual da transação no seu ciclo de vida.</summary>
    public TransactionStatus Status { get; private set; }

    /// <summary>Descrição livre fornecida pelo originador da transação.</summary>
    public string? Description { get; private set; }

    /// <summary>
    /// Motivo de falha ou cancelamento. Preenchido apenas quando Status é
    /// Failed ou Cancelled. Essencial para auditoria e suporte ao cliente.
    /// </summary>
    public string? FailureReason { get; private set; }

    /// <summary>
    /// Identificador de correlação externo (UUID v4) fornecido pelo cliente.
    /// Usado para garantir idempotência: mesma correlação = mesma transação.
    /// </summary>
    public Guid CorrelationId { get; private set; }

    /// <summary>Timestamp em que a transação começou a ser processada.</summary>
    public DateTime? ProcessedAt { get; private set; }

    // Navegação — populada pelo EF Core com Include() nas queries
    public Account? SourceAccount { get; private set; }
    public Account? DestinationAccount { get; private set; }

#pragma warning disable CS8618
    private Transaction() { }
#pragma warning restore CS8618

    /// <summary>
    /// Cria uma nova transação financeira no estado inicial Pending.
    /// </summary>
    /// <param name="sourceAccountId">Conta de origem (null para operações de crédito puro).</param>
    /// <param name="destinationAccountId">Conta de destino (null para operações de débito puro).</param>
    /// <param name="amountInCents">Valor em centavos. Deve ser estritamente positivo.</param>
    /// <param name="type">Tipo da operação financeira.</param>
    /// <param name="correlationId">UUID de idempotência fornecido pelo cliente.</param>
    /// <param name="description">Descrição opcional da transação.</param>
    /// <exception cref="ArgumentException">
    /// Lançada quando o valor é inválido ou as contas não são adequadas para o tipo.
    /// </exception>
    public Transaction(
        Guid? sourceAccountId,
        Guid? destinationAccountId,
        long amountInCents,
        string currency,
        TransactionType type,
        Guid correlationId,
        string? description = null)
    {
        ValidateAccounts(sourceAccountId, destinationAccountId, type);
        ValidateAmount(amountInCents);

        if (string.IsNullOrWhiteSpace(currency))
            throw new ArgumentException(
                "Código de moeda não pode ser vazio.",
                nameof(currency));

        if (correlationId == Guid.Empty)
            throw new ArgumentException(
                "CorrelationId não pode ser um Guid vazio.",
                nameof(correlationId));

        SourceAccountId = sourceAccountId;
        DestinationAccountId = destinationAccountId;
        AmountInCents = amountInCents;
        Currency = currency.Trim().ToUpperInvariant();
        Type = type;
        CorrelationId = correlationId;
        Description = description;
        // Toda transação nasce no estado Pending — aguardando processamento assíncrono
        Status = TransactionStatus.Pending;

        // O evento é emitido APÓS todas as validações passarem.
        // Só registramos o evento se o objeto foi criado em estado válido.
        // O Unit of Work (que será implementado ao código) vai coletar e publicar este evento após o SaveChanges bem-sucedido, garantindo consistência.
        AddDomainEvent(new TransactionCreatedEvent(
            TransactionId: Id,
            SourceAccountId: sourceAccountId,
            DestinationAccountId: destinationAccountId,
            AmountInCents: amountInCents,
            Type: type,
            CorrelationId: correlationId));
    }

    // ─────────────────────────────────────────────────────────────
    // MÁQUINA DE ESTADOS — Transições permitidas do ciclo de vida
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Marca a transação como em processamento.
    /// Só pode ser chamado a partir do estado Pending.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Lançada se a transação não estiver no estado Pending.
    /// </exception>
    public void StartProcessing()
    {
        EnsureStatus(
            TransactionStatus.Pending,
            "Apenas transações Pending podem iniciar o processamento.");

        Status = TransactionStatus.Processing;
        ProcessedAt = DateTime.UtcNow;
        SetUpdatedAt();
    }

    /// <summary>
    /// Marca a transação como concluída com sucesso.
    /// Só pode ser chamado a partir do estado Processing.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Lançada se a transação não estiver no estado Processing.
    /// </exception>
    public void Complete()
    {
        EnsureStatus(
            TransactionStatus.Processing,
            "Apenas transações em Processing podem ser concluídas.");

        Status = TransactionStatus.Completed;
        SetUpdatedAt();

        AddDomainEvent(new TransactionCompletedEvent(
            TransactionId: Id,
            CorrelationId: CorrelationId,
            AmountInCents: AmountInCents,
            CompletedAt: UpdatedAt));
    }

    /// <summary>
    /// Marca a transação como falha, registrando o motivo obrigatoriamente.
    /// Pode ser chamado a partir de Processing.
    /// </summary>
    /// <param name="reason">
    /// Motivo da falha. Obrigatório para fins de auditoria e suporte.
    /// </param>
    /// <exception cref="ArgumentException">Lançada se o motivo for vazio.</exception>
    /// <exception cref="InvalidOperationException">
    /// Lançada se a transação não estiver no estado Processing.
    /// </exception>
    public void Fail(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException(
                "Motivo de falha é obrigatório para fins de auditoria.",
                nameof(reason));

        EnsureStatus(
            TransactionStatus.Processing,
            "Apenas transações em Processing podem ser marcadas como falha.");

        Status = TransactionStatus.Failed;
        FailureReason = reason;
        SetUpdatedAt();

        AddDomainEvent(new TransactionFailedEvent(
            TransactionId: Id,
            CorrelationId: CorrelationId,
            Reason: reason,
            FailedAt: UpdatedAt));
    }

    /// <summary>
    /// Cancela a transação antes do processamento.
    /// Só pode ser chamado a partir do estado Pending.
    /// </summary>
    /// <param name="reason">Motivo do cancelamento. Obrigatório para auditoria.</param>
    /// <exception cref="ArgumentException">Lançada se o motivo for vazio.</exception>
    /// <exception cref="InvalidOperationException">
    /// Lançada se a transação não estiver no estado Pending.
    /// </exception>
    public void Cancel(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException(
                "Motivo de cancelamento é obrigatório para fins de auditoria.",
                nameof(reason));

        EnsureStatus(
            TransactionStatus.Pending,
            "Apenas transações Pending podem ser canceladas. " +
            "Transações em processamento devem aguardar conclusão ou falha.");

        Status = TransactionStatus.Cancelled;
        FailureReason = reason;
        SetUpdatedAt();
    }

    /// <summary>
    /// Retorna o valor da transação em reais (formato decimal) para exibição.
    /// Internamente, o valor é armazenado em centavos para precisão financeira.
    /// </summary>
    public decimal GetAmountInReais() => AmountInCents / 100m;

    /// <summary>Indica se a transação está em um estado terminal (não pode mais mudar).</summary>
    public bool IsTerminal =>
        Status is TransactionStatus.Completed
            or TransactionStatus.Failed
            or TransactionStatus.Cancelled;

    // ─────────────────────────────────────────────────────────────
    // MÉTODOS PRIVADOS DE VALIDAÇÃO
    // ─────────────────────────────────────────────────────────────

    // IA diz: Extrair validações para métodos privados mantém o construtor
    // legível e as regras individuais focadas e nomeadas semanticamente.
    private static void ValidateAmount(long amountInCents)
    {
        if (amountInCents <= 0)
            throw new ArgumentException(
                "Valor da transação deve ser positivo. " +
                $"Recebido: {amountInCents} centavos.",
                nameof(amountInCents));
    }

    private static void ValidateAccounts(
        Guid? sourceAccountId,
        Guid? destinationAccountId,
        TransactionType type)
    {
        // IA diz: Cada tipo de transação tem um conjunto diferente de contas obrigatórias.
        // Estas regras são invariantes de negócio — violá-las seria um erro de domínio.
        switch (type)
        {
            case TransactionType.Transfer:
                if (sourceAccountId is null || destinationAccountId is null)
                    throw new ArgumentException(
                        "Transferências requerem conta de origem e de destino.");

                if (sourceAccountId == destinationAccountId)
                    throw new ArgumentException(
                        "Conta de origem e destino não podem ser iguais em uma transferência.");
                break;

            case TransactionType.Debit:
            case TransactionType.Payment:
                if (sourceAccountId is null)
                    throw new ArgumentException(
                        $"Operações do tipo {type} requerem conta de origem.");
                break;

            case TransactionType.Credit:
            case TransactionType.Refund:
                if (destinationAccountId is null)
                    throw new ArgumentException(
                        $"Operações do tipo {type} requerem conta de destino.");
                break;
        }
    }

    private void EnsureStatus(TransactionStatus expected, string errorMessage)
    {
        if (Status != expected)
            throw new InvalidOperationException(
                $"{errorMessage} Estado atual: {Status}.");
    }
}