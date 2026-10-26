using FinancialTransactionService.Domain.Common;

namespace FinancialTransactionService.Domain.Events;

/// <summary>
/// Evento disparado quando uma transação falha durante o processamento.
///
/// Consumers interessados neste evento:
/// - NotificationHandler: notifica o cliente sobre a falha.
/// - AlertHandler: dispara alerta para o time de operações
///   se a taxa de falha exceder um threshold (ex: > 5% em 5 minutos).
/// - DLQMonitor: correlaciona com mensagens na Dead Letter Queue.
/// </summary>
/// <param name="TransactionId">ID da transação que falhou.</param>
/// <param name="CorrelationId">ID de correlação para rastreabilidade.</param>
/// <param name="Reason">Motivo da falha, sendo obrigatório para auditoria.</param>
/// <param name="FailedAt">Timestamp UTC da falha.</param>
public sealed record TransactionFailedEvent(
    Guid TransactionId,
    Guid CorrelationId,
    string Reason,
    DateTime FailedAt
) : IDomainEvent
{
    /// <inheritdoc/>
    public Guid EventId { get; } = Guid.NewGuid();

    /// <inheritdoc/>
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}