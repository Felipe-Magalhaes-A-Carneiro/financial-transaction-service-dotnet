namespace FinancialTransactionService.Domain.Exceptions;

/// <summary>
/// Exceção base para todas as violações de regras de negócio do domínio financeiro.
///
/// Hierarquia de uso:
/// - DomainException: capturada pelo middleware global da API → HTTP 422
/// - Sub-classes específicas: capturadas por handlers para respostas detalhadas
///
/// Diferença crítica de design:
/// - DomainException = regra de negócio violada (esperado, tratável pelo cliente)
/// - Exception/SystemException = bug ou falha de infraestrutura (inesperado, HTTP 500)
/// </summary>
public abstract class DomainException : Exception
{
    /// <summary>
    /// Código de erro legível por máquina — usado por clientes da API
    /// para identificar o tipo de erro sem fazer parse de mensagem de texto.
    /// Ex: "INSUFFICIENT_FUNDS", "INVALID_TRANSACTION_STATE"
    /// </summary>
    public string ErrorCode { get; }

    protected DomainException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    protected DomainException(string errorCode, string message, Exception innerException)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }
}