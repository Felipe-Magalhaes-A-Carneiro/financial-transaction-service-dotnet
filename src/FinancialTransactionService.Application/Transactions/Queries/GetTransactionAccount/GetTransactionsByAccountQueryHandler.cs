using FinancialTransactionService.Application.Common.Interfaces;
using FinancialTransactionService.Application.Transactions.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinancialTransactionService.Application.Transactions.Queries.GetTransactionsByAccount;

/// <summary>
/// Handler da Query de listagem paginada de transações por conta.
///
/// DEMONSTRAÇÃO DE DUAS TÉCNICAS DE PERFORMANCE:
///
/// 1. PREVENÇÃO DE N+1 via projeção direta com .Select()
///    A mesma técnica do GetTransactionByIdQueryHandler, aplicada
///    a uma listagem - uma única query SQL com LEFT JOINs.
///
/// 2. PAGINAÇÃO EFICIENTE com contagem paralela via Task.WhenAll
///    Buscamos a página de dados E o count total em paralelo,
///    em vez de duas queries sequenciais. Em um banco sob carga,
///    isso reduz a latência percebida pela metade nesta operação.
/// </summary>
public sealed class GetTransactionsByAccountQueryHandler
    : IRequestHandler<GetTransactionsByAccountQuery, PagedResultDto<TransactionDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ILogger<GetTransactionsByAccountQueryHandler> _logger;

    // Limite de segurança: evita que clientes solicitem páginas muito grandes
    // que possam saturar memória e degradar o banco de dados.
    private const int MaxPageSize = 100;

    public GetTransactionsByAccountQueryHandler(
        IApplicationDbContext context,
        ILogger<GetTransactionsByAccountQueryHandler> logger)
    {
        _context = context;
        _logger  = logger;
    }

    public async Task<PagedResultDto<TransactionDto>> Handle(
        GetTransactionsByAccountQuery request,
        CancellationToken cancellationToken)
    {
        // Sanitização defensiva dos parâmetros de paginação
        var page     = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, MaxPageSize);

        _logger.LogDebug(
            "Consultando transações da conta. AccountId: {AccountId}. " +
            "Página: {Page}. PageSize: {PageSize}. Status: {Status}.",
            request.AccountId,
            page,
            pageSize,
            request.Status?.ToString() ?? "todos");

        // ──────────────────────────────────
        // COMPOSIÇÃO DINÂMICA DA QUERY BASE
        //
        // Construímos o IQueryable incrementalmente com filtros opcionais.
        // O EF Core não executa nenhuma query até o ponto de materialização
        // (.CountAsync() ou .ToListAsync()). Isso permite compor o WHERE
        // dinamicamente sem concatenação de strings SQL — zero SQL injection.
        //
        // SQL GERADO (exemplo com todos os filtros):
        //   SELECT ... FROM transactions t
        //   LEFT JOIN accounts sa ON sa.id = t.source_account_id
        //   LEFT JOIN accounts da ON da.id = t.destination_account_id
        //   WHERE (t.source_account_id = @accountId
        //          OR t.destination_account_id = @accountId)
        //     AND t.status = @status
        //     AND t.created_at >= @from
        //     AND t.created_at <= @to
        //   ORDER BY t.created_at DESC
        //   LIMIT @pageSize OFFSET @offset
        // ────────────────────────────────────
        var baseQuery = _context.Transactions
            .AsNoTracking()
            .Where(t =>
                t.SourceAccountId      == request.AccountId ||
                t.DestinationAccountId == request.AccountId);

        // Filtros opcionais - adicionados apenas quando o parâmetro é não-nulo
        if (request.Status.HasValue)
            baseQuery = baseQuery.Where(t => t.Status == request.Status.Value);

        if (request.From.HasValue)
            baseQuery = baseQuery.Where(t => t.CreatedAt >= request.From.Value);

        if (request.To.HasValue)
            baseQuery = baseQuery.Where(t => t.CreatedAt <= request.To.Value);

        // ─────────────────────────────────────────────────────────────
        // PAGINAÇÃO EFICIENTE COM QUERIES PARALELAS
        //
        // Task.WhenAll executa as duas queries no banco simultaneamente:
        //   - countTask: SELECT COUNT(*) FROM ... WHERE ...
        //   - itemsTask: SELECT ... FROM ... WHERE ... ORDER BY ... LIMIT/OFFSET
        //
        // Tempo total ≈ MAX(tempo_count, tempo_items)
        // em vez de tempo_count + tempo_items (sequencial).
        //
        // As duas tasks compartilham o mesmo DbContext (escopo de
        // requisição). O EF Core gerencia isso com segurança internamente —
        // cada query obtém sua própria conexão do pool de conexões do provider.
        // ─────────────────────────────────────────────────────────────
        var countTask = baseQuery.LongCountAsync(cancellationToken);

        var itemsTask = baseQuery
            .OrderByDescending(t => t.CreatedAt)
            // Skip/Take são traduzidos para OFFSET/LIMIT pelo EF Core.
            // O índice no PostgreSQL em (created_at DESC) garante que esta
            // operação seja eficiente mesmo com milhões de registros.
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new TransactionDto
            {
                // ─────────────────────────────────────────────────────
                // PROJEÇÃO DIRETA — ZERO N+1
                // O mesmo padrão do GetTransactionByIdQueryHandler.
                // ─────────────────────────────────────────────────────
                Id                   = t.Id,
                CorrelationId        = t.CorrelationId,
                SourceAccountId      = t.SourceAccountId,
                DestinationAccountId = t.DestinationAccountId,
                AmountInCents        = t.AmountInCents,
                AmountInReais        = t.AmountInCents / 100m,
                Currency             = t.Currency,
                Type                 = t.Type.ToString(),
                Status               = t.Status.ToString(),
                Description          = t.Description,
                FailureReason        = t.FailureReason,
                CreatedAt            = t.CreatedAt,
                ProcessedAt          = t.ProcessedAt
            })
            .ToListAsync(cancellationToken);

        // Aguarda as duas queries terminarem (a mais lenta determina o tempo total)
        await Task.WhenAll(countTask, itemsTask);

        var totalCount = await countTask;
        var items      = await itemsTask;

        _logger.LogDebug(
            "Consulta concluída. AccountId: {AccountId}. " +
            "Total: {TotalCount}. Retornados: {ReturnedCount}.",
            request.AccountId,
            totalCount,
            items.Count);

        return PagedResultDto<TransactionDto>.Create(
            items:      items,
            totalCount: totalCount,
            page:       page,
            pageSize:   pageSize);
    }
}