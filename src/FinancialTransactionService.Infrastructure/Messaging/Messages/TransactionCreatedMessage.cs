using FinancialTransactionService.Domain.Enums;

namespace FinancialTransactionService.Infrastructure.Messaging.Messages;

/// <summary>
/// Mensagem publicada no broker (RabbitMQ) após a criação bem-sucedida
/// de uma transação financeira.
/// </summary>
public sealed record TransactionCreatedMessage(
    Guid MessageId,
    Guid TransactionId,
    Guid? SourceAccountId,
    Guid? DestinationAccountId,
    long AmountInCents,
    string Currency,
    TransactionType Type,
    Guid CorrelationId,
    DateTime OccurredOn
);