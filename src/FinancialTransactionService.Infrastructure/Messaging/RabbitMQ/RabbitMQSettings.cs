namespace FinancialTransactionService.Infrastructure.Messaging.RabbitMQ;

/// <summary>
/// Configurações tipadas para conexão e topologia do RabbitMQ.
/// Populadas via IConfiguration a partir de appsettings.json /
/// variáveis de ambiente no formato RabbitMQ__Host, RabbitMQ__Username, etc.
///
/// Validadas na inicialização da aplicação pelo InfrastructureServiceExtensions
/// via options.ValidateDataAnnotations() - falha fast se configuração inválida.
/// 
/// </summary>
public sealed class RabbitMQSettings
{
    /// <summary> Seção do appsettings que mapeia para esta classe.</summary>
    public const string SectionName = "RabbitMQ";

    /// <summary> Hostname ou IP do broker RabbitMQ.</summary>
    public string Host { get; init; } = "localhost";

    /// <summary> Porta AMQP. Padrão: 5672.</summary>
    public int Port { get; init; } = 5672;

    /// <summary> Virtual host configurado no broker.</summary>
    public string VirtualHost { get; init; } = "financial-vhost";

    /// <summary> Usuário de autenticação AMQP.</summary>
    public string Username { get; init; } = string.Empty;

    /// <summary> Senha de autenticação AMQP.</summary>
    public string Password { get; init; } = string.Empty;

    /// <summary> Nome da Exchange Direct principal.</summary>
    public string ExchangeName { get; init; } = "transactions.exchange";

    /// <summary> Nome da fila de processamento principal.</summary>
    public string QueueName { get; init; } = "transactions.process.queue";

    /// <summary> Nome da Dead Letter Queue para mensagens falhas.</summary>
    public string DeadLetterQueueName { get; init; } = "transactions.dead.queue";

    /// <summary>
    /// Timeout de conexão em segundos.
    /// Em ambientes containerizados, o RabbitMQ pode demorar a inicializar.
    /// 
    /// </summary>
    public int ConnectionTimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// Número máximo de tentativas de reconexão antes de lançar exceção.
    /// 
    /// </summary>
    public int MaxConnectionRetries { get; init; } = 5;
}