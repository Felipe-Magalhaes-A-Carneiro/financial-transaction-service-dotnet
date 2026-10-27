namespace FinancialTransactionService.Application.Common.Interfaces;

/// <summary>
/// Contrato para publicação de mensagens em um broker de mensagens.
///
/// Design deliberado: por que a interface é genérica (sem mencionar RabbitMQ)?
/// A Application define O QUE precisa (publicar uma mensagem) sem saber COMO (RabbitMQ, Kafka, Azure Service Bus, etc.).
/// Trocar o broker em produção não requer nenhuma mudança na Application.
///
/// Implementação concreta: RabbitMQPublisher (será implementado no futuro).
/// </summary>
/// 
public interface IMessagePublisher
{
    /// <summary>
    /// Publica uma mensagem serializada como JSON no broker configurado.
    /// </summary>
    /// 
    /// <typeparam name="TMessage">
    /// Tipo da mensagem. Deve ser serializável como JSON.
    /// Convenção de nomenclatura: sufixo "Message" (ex: TransactionCreatedMessage).
    /// </typeparam>
    /// 
    /// <param name="message">O objeto da mensagem a ser publicado.</param>
    /// <param name="routingKey">
    /// Chave de roteamento que determina qual fila receberá a mensagem.
    /// No RabbitMQ com Direct Exchange, a fila é selecionada por binding
    /// exato com esta chave. Ex: "transactions.created".
    /// </param>
    /// <param name="cancellationToken">Token para cancelamento cooperativo.</param>
    Task PublishAsync<TMessage>(
        TMessage message,
        string routingKey,
        CancellationToken cancellationToken = default)
        where TMessage : class;

    /// <summary>
    /// Publica uma mensagem com metadados adicionais de rastreabilidade.
    /// Usado quando precisamos propagar CorrelationId e outros headers
    /// de observabilidade ao longo de toda a cadeia de mensagens.
    /// </summary>
    /// 
    /// <typeparam name="TMessage">Tipo da mensagem.</typeparam>
    /// 
    /// <param name="message">O objeto da mensagem a ser publicado.</param>
    /// <param name="routingKey">Chave de roteamento da mensagem.</param>
    /// <param name="headers">
    /// Headers adicionais da mensagem (ex: correlationId, causationId, timestamp).
    /// No RabbitMQ, estes headers são acessíveis pelo consumer para logging e tracing.
    /// </param>
    /// <param name="cancellationToken">Token para cancelamento cooperativo.</param>
    Task PublishAsync<TMessage>(
        TMessage message,
        string routingKey,
        Dictionary<string, object> headers,
        CancellationToken cancellationToken = default)
        where TMessage : class;
}