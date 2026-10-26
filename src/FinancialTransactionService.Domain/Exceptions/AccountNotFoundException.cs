namespace FinancialTransactionService.Domain.Exceptions;

/// <summary>
/// Será usada quando uma conta financeira referenciada em uma operação
/// não é encontrada no repositório.
///
/// Importante: esta exceção é lançada pela camada de Application
/// (nos Command Handlers) após consultar o repositório e não pelo domínio puro.
/// Vive no projeto Domain porque é uma regra de negócio: referenciar uma
/// conta inexistente é uma violação de invariante do sistema financeiro.
/// </summary>
public sealed class AccountNotFoundException : DomainException
{
    /// <summary> ID da conta que não foi localizada. </summary>
    public Guid AccountId { get; }

    public AccountNotFoundException(Guid accountId)
        : base(
            errorCode: "ACCOUNT_NOT_FOUND",
            message: $"Conta com Id '{accountId}' não foi encontrada.")
    {
        AccountId = accountId;
    }

    // Sobrecarga conveniente para busca por número de conta (string),
    // usada quando o caller tem o número legível, não o UUID interno.
    public AccountNotFoundException(string accountNumber)
        : base(
            errorCode: "ACCOUNT_NOT_FOUND",
            message: $"Conta com número '{accountNumber}' não foi encontrada.")
    {
        AccountId = Guid.Empty;
    }
}