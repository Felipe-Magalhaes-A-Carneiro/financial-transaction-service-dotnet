namespace FinancialTransactionService.Application.Transactions.DTOs;

/// <summary>
/// Wrapper genérico para resultados paginados.
/// Retorna os dados da página atual junto com metadados de paginação
/// que permitem ao cliente navegar entre páginas sem consultas adicionais.
///
/// Padrão de resposta alinhado com as melhores práticas de API RESTful:
/// os metadados de paginação ficam no envelope, não em headers HTTP,
/// facilitando o consumo por clientes que não leem headers facilmente.
/// </summary>
/// 
/// <typeparam name="T">Tipo dos itens da página.</typeparam>
public sealed record PagedResultDto<T>
{
    /// <summary>
    /// Itens da página atual.
    /// </summary>
    public IReadOnlyList<T> Items { get; init; } = [];

    /// <summary>
    /// Página atual (base 1).
    /// </summary>
    public int Page { get; init; }

    /// <summary>
    /// Quantidade de itens por página solicitada.
    /// </summary>
    public int PageSize { get; init; }

    /// <summary>
    /// Total de itens que satisfazem o filtro (em todas as páginas).
    /// </summary>
    public long TotalCount { get; init; }

    /// <summary>
    /// Total de páginas calculado a partir de TotalCount e PageSize.
    /// </summary>
    public int TotalPages => PageSize > 0
        ? (int)Math.Ceiling((double)TotalCount / PageSize)
        : 0;

    /// <summary>
    /// Indica se existe uma página anterior.
    /// </summary>
    public bool HasPreviousPage => Page > 1;

    /// <summary>
    /// Indica se existe uma próxima página.
    /// </summary>
    public bool HasNextPage => Page < TotalPages;

    /// <summary>
    /// Factory method para criação padronizada de resultados paginados.
    /// </summary>
    public static PagedResultDto<T> Create(
        IReadOnlyList<T> items,
        long totalCount,
        int page,
        int pageSize) => new()
    {
        Items      = items,
        TotalCount = totalCount,
        Page       = page,
        PageSize   = pageSize
    };
}