using FinancialTransactionService.Domain.Entities;
using FinancialTransactionService.Domain.Enums;
using FinancialTransactionService.Domain.Interfaces;
using FinancialTransactionService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialTransactionService.Infrastructure.Persistence.Repositories;

/// <summary>
/// Implementação concreta do ITransactionRepository usando EF Core + PostgreSQL.
///
/// Padrões aplicados nesta implementação:
///
/// 1. AsNoTracking() em todas as queries de leitura pura — entidades
///    retornadas por repositórios de leitura nunca serão modificadas
///    diretamente; modificações passam pelo handler que carrega a entidade
///    com tracking ativo.
///
/// 2. Projeção com Select() onde possível para evitar N+1 e carregar
///    apenas os dados necessários.
///
/// 3. CancellationToken propagado em todas as operações async para
///    suporte correto a cancelamento de requisições HTTP.
/// </summary>
public sealed class TransactionRepository : ITransactionRepository
{
    private readonly ApplicationDbContext _context;

    public TransactionRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    // ─────────────────────────────────────────────────────────────────────
    // ESCRITA
    // ─────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task AddAsync(
        Transaction transaction,
        CancellationToken cancellationToken = default)
    {
        // IA diz: AddAsync apenas registra a entidade no Change Tracker
        // com estado EntityState.Added. O INSERT no banco acontece apenas
        // quando IUnitOfWork.SaveChangesAsync() é chamado pelo handler.
        // Isso permite adicionar múltiplas entidades antes de persistir tudo
        // em uma única transação de banco de dados.
        await _context.Transactions.AddAsync(transaction, cancellationToken);
    }

    // ─────────────────────────────────────────────────────────────────────
    // LEITURA
    // ─────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<Transaction?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        // IA diz: NÃO usamos AsNoTracking() aqui intencionalmente.
        // Este método é usado pelo ProcessTransactionCommandHandler que
        // MODIFICA a entidade (StartProcessing, Complete, Fail).
        // O EF Core precisa rastrear a entidade para detectar mudanças
        // e gerar o UPDATE correto no SaveChanges.
        return await _context.Transactions
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Transaction?> GetByCorrelationIdAsync(
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        // IA diz: AsNoTracking() aqui porque este método é usado para
        // verificar existência e ler dados — nunca para modificar.
        return await _context.Transactions
            .AsNoTracking()
            .FirstOrDefaultAsync(
                t => t.CorrelationId == correlationId,
                cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<Transaction>> GetByAccountIdAsync(
        Guid accountId,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        // ─────────────────────────────────────────────────────────────
        // PREVENÇÃO EXPLÍCITA DE N+1
        //
        // ❌ PROBLEMA — se fizéssemos assim:
        //    var transactions = await _context.Transactions
        //        .Where(t => t.SourceAccountId == accountId
        //                 || t.DestinationAccountId == accountId)
        //        .ToListAsync();
        //    // Acesso a t.SourceAccount → lazy load → N queries extras!
        //
        // ✅ SOLUÇÃO — Include() faz JOIN explícito:
        //    Uma única query com LEFT JOINs carrega tudo necessário.
        //    O SQL gerado será:
        //
        //    SELECT t.*, sa.*, da.*
        //    FROM transactions t
        //    LEFT JOIN accounts sa ON sa.id = t.source_account_id
        //    LEFT JOIN accounts da ON da.id = t.destination_account_id
        //    WHERE t.source_account_id = @id OR t.destination_account_id = @id
        //    ORDER BY t.created_at DESC
        //    LIMIT @pageSize OFFSET @offset
        // ─────────────────────────────────────────────────────────────
        return await _context.Transactions
            .AsNoTracking()
            .Include(t => t.SourceAccount)
            .Include(t => t.DestinationAccount)
            .Where(t =>
                t.SourceAccountId      == accountId ||
                t.DestinationAccountId == accountId)
            .OrderByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<Transaction>> GetByStatusAndDateRangeAsync(
        TransactionStatus status,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default)
    {
        return await _context.Transactions
            .AsNoTracking()
            .Where(t =>
                t.Status     == status      &&
                t.CreatedAt  >= from        &&
                t.CreatedAt  <= to)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    // ─────────────────────────────────────────────────────────────────────
    // VERIFICAÇÃO
    // ─────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<bool> ExistsByCorrelationIdAsync(
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        // IA diz: AnyAsync() gera um EXISTS no SQL — a query mais eficiente
        // para verificação de existência. NÃO carrega nenhum dado da linha,
        // apenas retorna true/false assim que encontra a primeira ocorrência.
        //
        // SQL gerado:
        //   SELECT EXISTS (
        //     SELECT 1 FROM transactions
        //     WHERE correlation_id = @correlationId
        //     LIMIT 1
        //   )
        return await _context.Transactions
            .AnyAsync(
                t => t.CorrelationId == correlationId,
                cancellationToken);
    }
}