using FinancialTransactionService.Application.Transactions.DTOs;
using MediatR;

namespace FinancialTransactionService.Application.Transactions.Queries.GetTransactionById;

/// <summary>
/// Query CQRS para busca de uma transação financeira por ID.
///
/// Implementa IRequest&lt;TransactionDto?&gt;:
/// - Retorna TransactionDto quando encontrada.
/// - Retorna null quando não encontrada (o controller decide o HTTP 404).
///   Preferimos null a lançar exceção em queries — não encontrar um recurso
///   em uma leitura não é necessariamente um erro de negócio.
/// </summary>
/// 
/// <param name="TransactionId">UUID da transação a ser consultada.</param>
public sealed record GetTransactionByIdQuery(
    Guid TransactionId
) : IRequest<TransactionDto?>;