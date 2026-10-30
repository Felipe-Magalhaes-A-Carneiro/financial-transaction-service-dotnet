using FinancialTransactionService.Application.Transactions.DTOs;
using FinancialTransactionService.Domain.Enums;
using MediatR;

namespace FinancialTransactionService.Application.Transactions.Queries.GetTransactionsByAccount;

/// <summary>
/// Query CQRS para listagem paginada das transações de uma conta.
/// Suporta filtros opcionais por status e intervalo de datas.
///
/// Design de parâmetros opcionais:
/// Parâmetros null significam "sem filtro para este campo".
/// Isso evita a explosão de métodos de query (GetByStatus, GetByDate,
/// GetByStatusAndDate...) — um único handler trata todas as combinações.
/// </summary>
/// 
/// <param name="AccountId">ID da conta cujas transações serão listadas.</param>
/// <param name="Page">Página solicitada (base 1). Padrão: 1.</param>
/// <param name="PageSize">Itens por página. Padrão: 20. Máximo: 100.</param>
/// <param name="Status">Filtro opcional por status da transação.</param>
/// <param name="From">Filtro opcional: data inicial do intervalo (UTC).</param>
/// <param name="To">Filtro opcional: data final do intervalo (UTC).</param>
public sealed record GetTransactionsByAccountQuery(
    Guid AccountId,
    int Page = 1,
    int PageSize = 20,
    TransactionStatus? Status = null,
    DateTime? From = null,
    DateTime? To = null
) : IRequest<PagedResultDto<TransactionDto>>;