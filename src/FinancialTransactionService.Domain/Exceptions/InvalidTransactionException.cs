using FinancialTransactionService.Domain.Enums;

namespace FinancialTransactionService.Domain.Exceptions;

/// <summary>
/// Lançada quando uma operação é solicitada sobre uma transação
/// em estado incompatível com a operação desejada.
///
/// Ex: tentar aprovar uma transação já cancelada,
/// ou processar uma transação já concluída.
/// </summary>
public sealed class InvalidTransactionException : DomainException
{
    /// <summary> ID da transação em estado inválido para a operação.</summary>
    public Guid TransactionId { get; }

    /// <summary> Estado atual da transação no momento da tentativa inválida.</summary>
    public TransactionStatus CurrentStatus { get; }

    /// <summary> Operação que foi tentada e é incompatível com o estado atual.</summary>
    public string AttemptedOperation { get; }

    public InvalidTransactionException(
        Guid transactionId,
        TransactionStatus currentStatus,
        string attemptedOperation)
        : base(
            errorCode: "INVALID_TRANSACTION_STATE",
            message: $"Operação '{attemptedOperation}' inválida para a transação '{transactionId}'. " +
                     $"Estado atual: {currentStatus}. " +
                     $"Verifique a máquina de estados permitida para esta operação.")
    {
        TransactionId = transactionId;
        CurrentStatus = currentStatus;
        AttemptedOperation = attemptedOperation;
    }
}