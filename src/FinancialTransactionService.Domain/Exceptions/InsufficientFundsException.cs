namespace FinancialTransactionService.Domain.Exceptions;

/// <summary>
/// Lançada quando uma operação de débito ou transferência não pode ser
/// executada por insuficiência de saldo na conta de origem.
///
/// Esta exceção carrega contexto rico para logging estruturado:
/// o handler de exceção consegue registrar AccountId, valor disponível
/// e valor solicitado sem fazer parse de string de mensagem.
/// </summary>
public sealed class InsufficientFundsException : DomainException
{
    /// <summary> ID da conta com saldo insuficiente.</summary>
    public Guid AccountId {get; }

    /// <summary> Saldo disponível no momento da tentativa (em centavos).</summary>
    public long AvailableBalanceInCents {get; }

    /// <summary> Valor que foi solicitado para débito (em centavos).</summary>
    public long RequestedAmountInCents {get; }

    public InsufficientFundsException(
        Guid accountId,
        long availableBalanceInCents,
        long requestedAmountInCents)
        : base(
            errorCode: "INSUFFICIENT_FUNDS",
            message: $"Saldo insuficiente na conta '{accountId}'. " +
                     $"Disponível: {availableBalanceInCents} centavos. " +
                     $"Solicitado: {requestedAmountInCents} centavos.")
    {
        AccountId = accountId;
        AvailableBalanceInCents = availableBalanceInCents;
        RequestedAmountInCents = requestedAmountInCents;
    }
}