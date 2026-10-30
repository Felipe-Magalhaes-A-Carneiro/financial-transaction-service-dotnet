using FinancialTransactionService.Application.Common.Interfaces;
using FinancialTransactionService.Application.Transactions.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinancialTransactionService.Application.Transactions.Queries.GetTransactionById;

/// <summary>
/// Handler da Query de busca de transação por ID.
///
/// DEMONSTRAÇÃO EXPLÍCITA DE PREVENÇÃO DE N+1:
/// Ao invés de buscar a entidade Transaction e depois acessar
/// SourceAccount e DestinationAccount via propriedades de navegação
/// (o que causaria lazy loading = N+1), usamos .Select() para projetar
/// diretamente do banco para o DTO em uma única query SQL com LEFT JOINs.
/// </summary>
public sealed class GetTransactionByIdQueryHandler
    : IRequestHandler<GetTransactionByIdQuery, TransactionDto?>
{
    private readonly IApplicationDbContext _context;
    private readonly ILogger<GetTransactionByIdQueryHandler> _logger;

    public GetTransactionByIdQueryHandler(
        IApplicationDbContext context,
        ILogger<GetTransactionByIdQueryHandler> logger)
    {
        _context = context;
        _logger  = logger;
    }

    public async Task<TransactionDto?> Handle(
        GetTransactionByIdQuery request,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Consultando transação por ID. TransactionId: {TransactionId}.",
            request.TransactionId);

        // ─────────────────────────────────────────────────────────────────────
        // PROJEÇÃO DIRETA PARA DTO — ZERO N+1
        //
        // O EF Core traduz este .Select() para SQL puro com LEFT JOINs,
        // buscando apenas as colunas necessárias para o DTO.
        //
        // SQL GERADO (aproximado):
        //   SELECT
        //       t.id, t.correlation_id, t.source_account_id,
        //       t.destination_account_id, t.amount_in_cents, t.currency,
        //       t.type, t.status, t.description, t.failure_reason,
        //       t.created_at, t.processed_at,
        //       sa.id AS source_id,   sa.account_number AS source_number,
        //       da.id AS dest_id,     da.account_number AS dest_number
        //   FROM transactions t
        //   LEFT JOIN accounts sa ON sa.id = t.source_account_id
        //   LEFT JOIN accounts da ON da.id = t.destination_account_id
        //   WHERE t.id = @transactionId
        //   LIMIT 1
        //
        // UM ÚNICO SELECT — independente de quantas contas existam.
        // ─────────────────────────────────────────────────────────────────────
        var dto = await _context.Transactions
            .AsNoTracking()
            // AsNoTracking() é obrigatório em queries de leitura.
            // Sem ele, o EF Core rastreia as entidades no Change Tracker,
            // consumindo memória e CPU desnecessariamente para dados que
            // nunca serão modificados nesta operação.
            .Where(t => t.Id == request.TransactionId)
            .Select(t => new TransactionDto
            {
                Id                   = t.Id,
                CorrelationId        = t.CorrelationId,
                SourceAccountId      = t.SourceAccountId,
                DestinationAccountId = t.DestinationAccountId,
                AmountInCents        = t.AmountInCents,
                // A divisão decimal acontece no banco de dados (não em C#),
                // pois o EF Core traduz expressões simples para SQL.
                // PostgreSQL: amount_in_cents / 100.0 AS amount_in_reais
                AmountInReais        = t.AmountInCents / 100m,
                Currency             = t.Currency,
                // .ToString() em enums dentro de .Select() é traduzido
                // pelo EF Core para o valor string do enum via conversão de valor.
                // Configuraremos HasConversion<string>() futuramente.
                Type                 = t.Type.ToString(),
                Status               = t.Status.ToString(),
                Description          = t.Description,
                FailureReason        = t.FailureReason,
                CreatedAt            = t.CreatedAt,
                ProcessedAt          = t.ProcessedAt
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (dto is null)
        {
            _logger.LogDebug(
                "Transação não encontrada. TransactionId: {TransactionId}.",
                request.TransactionId);
        }

        return dto;
    }
}