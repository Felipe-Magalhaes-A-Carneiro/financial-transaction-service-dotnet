using FinancialTransactionService.Domain.Entities;
using FinancialTransactionService.Domain.Enums;

namespace FinancialTransactionService.Domain.Interfaces;

/// <summary>
/// Contrato de persistência para a entidade Transaction.
///
/// Design deliberado — o que NÃO está aqui e por quê:
/// - Sem métodos de Update() explícito: o EF Core rastreia mudanças
///   automaticamente via Change Tracker. Basta buscar, modificar e SaveChanges.
/// - Sem IQueryable exposto: expor IQueryable vaza detalhes do ORM para
///   a camada de Application. Cada query tem seu método semântico nomeado.
/// - Sem métodos de paginação complexa: queries de leitura com projeção
///   para DTO serão feitas diretamente no EF Core nos QueryHandlers (Cap. 3/4),
///   evitando o carregamento desnecessário de entidades completas.
/// </summary>
public interface ITransactionRepository
{
    // ─────────────────────────────────────────────────────────────
    // OPERAÇÕES DE ESCRITA
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Adiciona uma nova transação ao contexto de persistência.
    /// A gravação efetiva ocorre em IUnitOfWork.SaveChangesAsync().
    /// </summary>
    /// <param name="transaction">Entidade a ser persistida.</param>
    /// <param name="cancellationToken">Token para cancelamento cooperativo.</param>
    Task AddAsync(Transaction transaction, CancellationToken cancellationToken = default);

    // ─────────────────────────────────────────────────────────────
    // OPERAÇÕES DE LEITURA
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Busca uma transação pelo seu identificador único.
    /// Retorna null se não encontrada — o caller decide como tratar ausência.
    /// </summary>
    /// <param name="id">Identificador UUID da transação.</param>
    /// <param name="cancellationToken">Token para cancelamento cooperativo.</param>
    /// <returns>A entidade Transaction ou null.</returns>
    Task<Transaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Busca uma transação pelo seu CorrelationId externo.
    /// Usado pelo IdempotencyMiddleware para detectar requisições duplicadas
    /// que já foram processadas e persistidas.
    /// </summary>
    /// <param name="correlationId">UUID de correlação fornecido pelo cliente.</param>
    /// <param name="cancellationToken">Token para cancelamento cooperativo.</param>
    /// <returns>A transação existente ou null se for a primeira ocorrência.</returns>
    Task<Transaction?> GetByCorrelationIdAsync(
        Guid correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lista transações de uma conta específica com suporte a paginação.
    /// Retorna transações de origem OU destino da conta informada.
    ///
    /// NOTA DE PERFORMANCE: A implementação concreta (Cap. 4) deve usar
    /// projeção para DTO via .Select() para evitar N+1 e carregamento
    /// desnecessário de propriedades de navegação completas.
    /// </summary>
    /// <param name="accountId">ID da conta (origem ou destino).</param>
    /// <param name="page">Número da página (base 1).</param>
    /// <param name="pageSize">Itens por página. Máximo recomendado: 100.</param>
    /// <param name="cancellationToken">Token para cancelamento cooperativo.</param>
    Task<IEnumerable<Transaction>> GetByAccountIdAsync(
        Guid accountId,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lista transações em um status específico dentro de um intervalo de datas.
    /// Usado pelo Background Service para reprocessamento e pelo monitoring.
    /// </summary>
    /// <param name="status">Status alvo para filtro.</param>
    /// <param name="from">Data inicial do intervalo (UTC).</param>
    /// <param name="to">Data final do intervalo (UTC).</param>
    /// <param name="cancellationToken">Token para cancelamento cooperativo.</param>
    Task<IEnumerable<Transaction>> GetByStatusAndDateRangeAsync(
        TransactionStatus status,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default);

    // ─────────────────────────────────────────────────────────────
    // OPERAÇÕES DE VERIFICAÇÃO
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Verifica se já existe uma transação com o CorrelationId informado.
    /// Mais eficiente que GetByCorrelationIdAsync quando só precisamos
    /// saber da existência — usa EXISTS na query SQL, sem carregar a entidade.
    /// </summary>
    /// <param name="correlationId">UUID de correlação a verificar.</param>
    /// <param name="cancellationToken">Token para cancelamento cooperativo.</param>
    /// <returns>True se já existe uma transação com este CorrelationId.</returns>
    Task<bool> ExistsByCorrelationIdAsync(
        Guid correlationId,
        CancellationToken cancellationToken = default);
}