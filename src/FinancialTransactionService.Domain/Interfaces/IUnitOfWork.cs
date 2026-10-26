namespace FinancialTransactionService.Domain.Interfaces;

/// <summary>
/// Contrato que abstrai a unidade de trabalho transacional.
///
/// O Unit of Work agrupa múltiplas operações de repositório em uma
/// única transação atômica: ou todas persistem, ou nenhuma persiste.
///
/// Responsabilidade crítica neste sistema:
/// Após o SaveChangesAsync bem-sucedido, o UoW coleta os Domain Events de todas as entidades modificadas e os despacha, garantindo que eventos só são publicados SE e SOMENTE SE os dados foram salvos.
/// Isso evita o problema de publicar um evento RabbitMQ para uma transação que falhou no banco de dados.
/// </summary>
public interface IUnitOfWork : IDisposable
{
    /// <summary>
    /// Persiste todas as alterações rastreadas e despacha os Domain Events
    /// das entidades modificadas.
    /// </summary>
    /// <param name="cancellationToken"> Token para cancelamento cooperativo.</param>
    /// <returns> Número de entidades afetadas na operação de persistência.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Inicia uma transação de banco de dados explícita.
    /// Usado quando múltiplas operações de SaveChanges precisam ser atômicas
    /// (ex: debitar conta origem E creditar conta destino na mesma transação DB).
    /// </summary>
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Confirma (commit) a transação de banco de dados explícita.
    /// </summary>
    Task CommitTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Desfaz (rollback) a transação de banco de dados explícita. Chamado em blocos catch para garantir consistência em caso de falha.
    /// </summary>
    Task RollbackTransactionAsync(CancellationToken cancellationToken = default);
}