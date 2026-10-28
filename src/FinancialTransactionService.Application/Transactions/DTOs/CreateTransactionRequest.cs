using FinancialTransactionService.Domain.Enums;

namespace FinancialTransactionService.Application.Transactions.DTOs;

/// <summary>
/// Modelo de entrada para criação de transação via HTTP POST.
/// Representa exatamente o body JSON esperado pelo endpoint.
///
/// Separação deliberada de Request <-> Command:
/// - Request: contrato público da API (pode mudar com versionamento)
/// - Command: contrato interno do MediatR (desacoplado da API)
/// O controller mapeia Request -> Command, adicionando o CorrelationId do header.
/// </summary>
/// 
/// <param name="SourceAccountId">
/// Conta de origem. Obrigatória para Transfer, Debit e Payment.
/// </param>
/// <param name="DestinationAccountId">
/// Conta de destino. Obrigatória para Transfer, Credit e Refund.
/// </param>
/// <param name="Amount">
/// Valor em reais com até 4 casas decimais (ex: 150.75).
/// Será convertido internamente para centavos via Money Value Object.
/// </param>
/// <param name="Currency"> Código ISO 4217 da moeda (ex: "BRL").</param>
/// <param name="Type">Tipo da operação financeira.</param>
/// <param name="Description">Descrição livre opcional. Máximo: 500 caracteres.</param>
public sealed record CreateTransactionRequest(
    Guid? SourceAccountId,
    Guid? DestinationAccountId,
    decimal Amount,
    string Currency,
    TransactionType Type,
    string? Description
);