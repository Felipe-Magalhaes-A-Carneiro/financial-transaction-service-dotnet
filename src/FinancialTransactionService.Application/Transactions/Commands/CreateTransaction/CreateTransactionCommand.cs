using FinancialTransactionService.Application.Transactions.DTOs;
using FinancialTransactionService.Domain.Enums;
using MediatR;

namespace FinancialTransactionService.Application.Transactions.Commands.CreateTransaction;

/// <summary>
/// Command CQRS para criação de uma transação financeira.
///
/// Implementa IRequest&lt;TransactionDto&gt;: o MediatR garante que este command
/// será despachado para CreateTransactionCommandHandler e retornará um TransactionDto.
///
/// Diferença entre Request e Command:
/// - CreateTransactionRequest: vem do body JSON da API (contrato público)
/// - CreateTransactionCommand: trafega internamente via MediatR (contrato interno)
/// O Command adiciona CorrelationId, extraído do header X-Correlation-Id pelo controller.
/// </summary>
/// 
/// <param name="SourceAccountId">Conta de origem. Null para Credit e Refund.</param>
/// <param name="DestinationAccountId">Conta de destino. Null para Debit e Payment.</param>
/// <param name="Amount">Valor em reais (ex: 150.75). Convertido para centavos no Handler.</param>
/// <param name="Currency">Código ISO 4217 (ex: "BRL"). Validado pelo Money Value Object.</param>
/// <param name="Type">Tipo da operação financeira.</param>
/// <param name="CorrelationId">UUID de idempotência extraído do header X-Correlation-Id.</param>
/// <param name="Description">Descrição opcional da transação.</param>
public sealed record CreateTransactionCommand(
    Guid? SourceAccountId,
    Guid? DestinationAccountId,
    decimal Amount,
    string Currency,
    TransactionType Type,
    Guid CorrelationId,
    string? Description
) : IRequest<TransactionDto>;