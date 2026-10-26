using FinancialTransactionService.Domain.Common;
using FinancialTransactionService.Domain.Enums;

namespace FinancialTransactionService.Domain.Events;

/// <summary>
/// Evento disparado imediatamente após a criação de uma nova transação financeira.
///
/// Consumers interessados neste evento (registrados via MediatR futuramente):
/// - RabbitMQPublisher: publica a mensagem na fila de processamento assíncrono.
/// - AuditHandler (futuro): registra entrada no log de auditoria regulatória.
/// 
/// </summary>
/// <param name="TransactionId">ID da transação recém-criada.</param>
/// <param name="SourceAccountId">Conta de origem (pode ser null conforme o tipo).</param>
/// <param name="DestinationAccountId">Conta de destino (pode ser null conforme o tipo).</param>
/// <param name="AmountInCents">Valor em centavos da transação.</param>
/// <param name="Type">Tipo da operação financeira.</param>
/// <param name="CorrelationId">ID de correlação para rastreabilidade fim-a-fim.</param>
public sealed record TransactionCreatedEvent(
    Guid TransactionId,
    Guid? SourceAccountId,
    Guid? DestinationAccountId,
    long AmountInCents,
    TransactionType Type,
    Guid CorrelationId
) : IDomainEvent
{
    /// <inheritdoc/>
    public Guid EventId { get; } = Guid.NewGuid();

    /// <inheritdoc/>
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}