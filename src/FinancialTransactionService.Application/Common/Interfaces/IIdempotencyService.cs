/// <summary>
/// Contrato para o serviço de idempotência baseado em chave/valor.
///
/// Idempotência garante que executar a mesma operação múltiplas vezes
/// produz o mesmo resultado que executá-la uma única vez — propriedade
/// fundamental em sistemas financeiros onde retries de rede são comuns.
///
/// Fluxo de uso no middleware (implementado no futuro):
///
///   1. Requisição chega com X-Correlation-Id: "uuid-abc"
///   2. IIdempotencyService.ExistsAsync("uuid-abc") -> false (primeira vez)
///   3. Processa a requisição normalmente
///   4. IIdempotencyService.SetAsync("uuid-abc", responseJson, ttl: 24h)
///   5. Segunda requisição com mesmo "uuid-abc" -> ExistsAsync -> true
///   6. IIdempotencyService.GetAsync("uuid-abc") -> retorna resposta cacheada
///   7. Cliente recebe exatamente a mesma resposta, sem reprocessamento
///
/// Implementação concreta: RedisIdempotencyService (será implementado no futuro).
/// </summary>


public interface IIdempotencyService
{
    /// <summary>
    /// Verifica se uma chave de idempotência já foi registrada.
    /// Implementado com EXISTS no Redis -> O(1), sem transferir dados.
    /// </summary>
    /// 
    /// <param name="key">
    /// Chave de idempotência (geralmente o CorrelationId da requisição).
    /// </param>
    /// <param name="cancellationToken">Token para cancelamento cooperativo.</param>
    /// 
    /// <returns>True se a chave já existe no cache.</returns>
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Recupera o valor associado a uma chave de idempotência.
    /// Usado para retornar a resposta cacheada de uma requisição já processada.
    /// 
    /// </summary>
    /// 
    /// <param name="key">Chave de idempotência registrada anteriormente.</param>
    /// <param name="cancellationToken">Token para cancelamento cooperativo.</param>
    /// 
    /// <returns>
    /// O valor serializado em JSON da resposta original, ou null se não existir.
    /// </returns>
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Registra uma chave de idempotência com seu valor e tempo de expiração.
    /// Chamado após o processamento bem-sucedido de uma requisição.
    /// </summary>
    /// <param name="key">Chave de idempotência (CorrelationId).</param>
    /// <param name="value">Resposta serializada em JSON a ser cacheada.</param>
    /// <param name="expiration">
    /// Tempo de vida da chave no Redis.
    /// Padrão de mercado para transações financeiras: 24 horas.
    /// Garante que retries dentro da janela de 24h recebam resposta cacheada.
    /// </param>
    /// <param name="cancellationToken">Token para cancelamento cooperativo.</param>
    Task SetAsync(
        string key,
        string value,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove uma chave de idempotência do cache.
    /// Usado em cenários de rollback onde a operação falhou após o registro
    /// da chave — evita que o cliente fique bloqueado por uma idempotência
    /// registrada para uma operação que não foi concluída.
    /// </summary>
    /// 
    /// <param name="key">Chave a ser removida.</param>
    /// <param name="cancellationToken">Token para cancelamento cooperativo.</param>
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
}