using FinancialTransactionService.Domain.Entities;

namespace FinancialTransactionService.Domain.Interfaces;

/// <summary>
/// Contrato de persistência para a entidade Account.
///
/// Accounts são entidades de vida longa e alta frequência de leitura.
/// 
/// O design da interface reflete isso: múltiplos métodos de busca
/// por chaves diferentes (Id, número de conta) para atender os
/// diferentes contextos de uso sem comprometer a clareza semântica.
/// </summary>
public interface IAccountRepository
{
    // ────────────────────
    // OPERAÇÕES DE ESCRITA
    // ────────────────────
    /// <summary>
    /// Adiciona uma nova conta ao contexto de persistência.
    /// A gravação efetiva ocorre em IUnitOfWork.SaveChangesAsync().
    /// </summary>
    Task AddAsync(Account account, CancellationToken cancellationToken = default);

    // ─────────────────────
    // OPERAÇÕES DE LEITURA
    // ─────────────────────

    /// <summary>
    /// Busca uma conta pelo seu identificador interno (UUID).
    /// Uso primário: Command Handlers que recebem IDs via payload JSON.
    /// </summary>
    /// <returns> A entidade Account ou null se não encontrada.</returns>
    Task<Account?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Busca uma conta pelo seu número legível (ex: "0001-9").
    /// Uso primário: operações iniciadas por humanos via atendimento ou UI
    /// onde o número de conta é o identificador natural do negócio.
    /// </summary>
    /// <returns>A entidade Account ou null se não encontrada.</returns>
    Task<Account?> GetByAccountNumberAsync(
        string accountNumber,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Busca múltiplas contas pelos seus IDs em uma única query ao banco.
    ///
    /// PREVENÇÃO DE N+1: Essencial para o cenário de transferência,
    /// onde precisamos das contas de origem E destino. Sem este método, o handler faria duas queries sequenciais. Com este método, fazemos WHERE Id IN (@id1, @id2) — uma única query.
    /// </summary>
    /// 
    /// <param name="ids">Coleção de IDs a buscar.</param>
    /// <param name="cancellationToken">Token para cancelamento cooperativo.</param>
    /// 
    /// <returns>
    /// Dicionário indexado por Id para acesso O(1) por chave no handler.
    /// Contas não encontradas simplesmente não aparecem no dicionário.
    /// </returns>
    Task<Dictionary<Guid, Account>> GetByIdsAsync(
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken = default);

    // ─────────────────────────
    // OPERAÇÕES DE VERIFICAÇÃO
    // ─────────────────────────

    /// <summary>
    /// Verifica se um número de conta já está em uso. Usado na criação de novas contas para garantir unicidade sem precisar carregar a entidade completa. Usa EXISTS na query SQL — não carrega dados desnecessários.
    /// </summary>
    Task<bool> ExistsByAccountNumberAsync(
        string accountNumber,
        CancellationToken cancellationToken = default);
}