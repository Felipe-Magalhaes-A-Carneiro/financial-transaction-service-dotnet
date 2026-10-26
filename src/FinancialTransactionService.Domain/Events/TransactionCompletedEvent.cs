using FinancialTransactionService.Domain.Common;

namespace FinancialTransactionService.Domain.Events;

/// <summary>
/// Evento disparado quando uma transação é processada com sucesso.
///
/// Consumers interessados neste evento:
/// - NotificationHandler: envia push notification / e-mail ao cliente.
/// - StatementHandler: atualiza o extrato da conta em tempo real.
/// - CashbackHandler: calcula e credita cashback se aplicável.
/// </summary>
/// <param name="TransactionId">ID da transação concluída.</param>
/// <param name="CorrelationId">ID de correlação para rastreabilidade.</param>
/// <param name="AmountInCents">Valor processado em centavos.</param>
/// <param name="CompletedAt">Timestamp UTC da conclusão.</param>
public sealed record TransactionCompletedEvent(
    Guid TransactionId,
    Guid CorrelationId,
    long AmountInCents,
    DateTime CompletedAt
) : IDomainEvent
{
    /// <inheritdoc/>
    public Guid EventId {get; } = Guid.NewGuid();

    /// <inheritdoc/>
    public DateTime OccurredOn {get; } = DateTime.UtcNow;
}