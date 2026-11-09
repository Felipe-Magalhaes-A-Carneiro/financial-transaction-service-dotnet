using FinancialTransactionService.Domain.Entities;
using FinancialTransactionService.Domain.Interfaces;
using FinancialTransactionService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialTransactionService.Infrastructure.Persistence.Repositories;

/// <summary>
/// Implementação concreta do IAccountRepository usando EF Core + PostgreSQL.
///
/// Destaque: GetByIdsAsync() — método crítico para prevenção de N+1
/// nos handlers que precisam de múltiplas contas (ex: transferência).
/// Usa WHERE Id IN (@ids) em vez de múltiplos WHERE Id = @id,
/// resultando em uma única roundtrip ao banco independente do número de contas.
/// </summary>
public sealed class AccountRepository : IAccountRepository
{
    private readonly ApplicationDbContext _context;

    public AccountRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    // ────────
    // ESCRITA
    // ────────

    /// <inheritdoc/>
    public async Task AddAsync(
        Account account,
        CancellationToken cancellationToken = default)
    {
        await _context.Accounts.AddAsync(account, cancellationToken);
    }

    // ────────
    // LEITURA
    // ────────

    /// <inheritdoc/>
    public async Task<Account?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        // Sem AsNoTracking() — este método é usado pelos handlers
        // de Command que podem modificar o saldo da conta (Debit/Credit).
        // O Change Tracker precisa rastrear a entidade para gerar o UPDATE.
        return await _context.Accounts
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Account?> GetByAccountNumberAsync(
        string accountNumber,
        CancellationToken cancellationToken = default)
    {
        return await _context.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(
                a => a.AccountNumber == accountNumber,
                cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Dictionary<Guid, Account>> GetByIdsAsync(
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        // ─────────────────────────────────────────────────────────────
        // PREVENÇÃO EXPLÍCITA DE N+1 — CARGA EM LOTE
        //
        // ---ERRADO--- ANTI-PATTERN (N queries para N contas):
        //    var source = await repo.GetByIdAsync(sourceId);      // query 1
        //    var dest   = await repo.GetByIdAsync(destinationId); // query 2
        //    
        // Para N transferências paralelas: N*2 queries
        //
        // ---CORRETO--- PADRÃO (1 query para N contas):
        //    var accounts = await repo.GetByIdsAsync([sourceId, destId]);
        //
        // SQL gerado pelo EF Core:
        //   SELECT * FROM accounts
        //   WHERE id IN (@p0, @p1)
        //
        // O resultado é convertido para Dictionary<Guid, Account>
        // para acesso O(1) por chave no handler — sem LINQ adicional.
        // ─────────────────────────────────────────────────────────────
        var idList = ids.ToList();

        // Sem AsNoTracking() porque os handlers de processamento
        // precisam modificar os saldos das contas carregadas.
        // O Change Tracker rastreia todas as entidades retornadas.
        var accounts = await _context.Accounts
            .Where(a => idList.Contains(a.Id))
            .ToListAsync(cancellationToken);

        return accounts.ToDictionary(a => a.Id);
    }

    // ────────────
    // VERIFICAÇÃO
    // ────────────

    /// <inheritdoc/>
    public async Task<bool> ExistsByAccountNumberAsync(
        string accountNumber,
        CancellationToken cancellationToken = default)
    {
        // SQL gerado:
        //   SELECT EXISTS (
        //     SELECT 1 FROM accounts
        //     WHERE account_number = @accountNumber
        //     LIMIT 1
        //   )
        return await _context.Accounts
            .AnyAsync(
                a => a.AccountNumber == accountNumber,
                cancellationToken);
    }
}