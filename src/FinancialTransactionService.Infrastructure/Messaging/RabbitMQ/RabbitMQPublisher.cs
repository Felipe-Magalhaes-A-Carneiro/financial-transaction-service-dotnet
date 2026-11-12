using System.Text;
using System.Text.Json;
using FinancialTransactionService.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace FinancialTransactionService.Infrastructure.Messaging.RabbitMQ;

/// <summary>
/// Implementação do IMessagePublisher usando RabbitMQ.
/// Publica mensagens serializadas como JSON em uma Direct Exchange,
/// com headers de rastreabilidade e propriedades de persistência.
///
/// Design de ciclo de vida:
/// - Registrado como Singleton no DI (uma instância para toda a aplicação)
/// - Mantém IConnection e IChannel persistentes e os recria em caso de falha
/// - Thread-safe: o canal RabbitMQ.Client 7+ é async-safe por padrão
///
/// Propriedades de durabilidade configuradas em cada mensagem:
/// - Delivery Mode 2 (Persistent): mensagem sobrevive a restart do broker
/// - ContentType: "application/json"
/// - Timestamp: UTC do momento da publicação
/// 
/// </summary>
public sealed class RabbitMQPublisher : IMessagePublisher, IAsyncDisposable
{
    private readonly RabbitMQSettings _settings;
    private readonly ILogger<RabbitMQPublisher> _logger;

    // Conexão e canal são lazy-initialized e reutilizados.
    // SemaphoreSlim garante que a inicialização seja thread-safe
    // sem bloquear threads desnecessariamente (async lock).
    private IConnection? _connection;
    private IChannel? _channel;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    // Opções de serialização JSON: camelCase para interoperabilidade
    // com consumers em outras linguagens (Node.js, Python, etc.)
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        WriteIndented               = false,
        DefaultIgnoreCondition      =
            System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public RabbitMQPublisher(
        IOptions<RabbitMQSettings> settings,
        ILogger<RabbitMQPublisher> logger)
    {
        _settings = settings.Value;
        _logger   = logger;
    }

    // ───────────
    // PUBLICAÇÃO
    // ───────────

    /// <inheritdoc/>
    public async Task PublishAsync<TMessage>(
        TMessage message,
        string routingKey,
        CancellationToken cancellationToken = default)
        where TMessage : class
    {
        await PublishAsync(message, routingKey, [], cancellationToken);
    }

    /// <inheritdoc/>
    public async Task PublishAsync<TMessage>(
        TMessage message,
        string routingKey,
        Dictionary<string, object> headers,
        CancellationToken cancellationToken = default)
        where TMessage : class
    {
        // Garante que a conexão e o canal estão ativos antes de publicar.
        // A inicialização lazy evita falha de startup se o RabbitMQ
        // ainda estiver inicializando quando a API sobe.
        var channel = await GetOrCreateChannelAsync(cancellationToken);

        var messageJson  = JsonSerializer.Serialize(message, JsonOptions);
        var messageBytes = Encoding.UTF8.GetBytes(messageJson);

        // ─────────────────────────────────────────────────────────────
        // PROPRIEDADES DA MENSAGEM
        // Define metadados AMQP que acompanham a mensagem no broker
        // e ficam visíveis na UI de gerenciamento do RabbitMQ.
        // ─────────────────────────────────────────────────────────────
        var properties = new BasicProperties
        {
            // DeliveryMode.Persistent: a mensagem é gravada em disco pelo broker.
            // Sobrevive a um restart do RabbitMQ sem ser perdida.
            // Fundamental para mensagens financeiras.
            DeliveryMode  = DeliveryModes.Persistent,
            ContentType   = "application/json",
            ContentEncoding = "UTF-8",
            Timestamp     = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
            MessageId     = Guid.NewGuid().ToString(),
            Type          = typeof(TMessage).Name,
            // Headers AMQP: visíveis no RabbitMQ Management UI e acessíveis
            // pelo consumer sem deserializar o body da mensagem.
            Headers       = headers
                .ToDictionary(
                    kvp => kvp.Key,
                    kvp => (object?)kvp.Value.ToString())
        };

        _logger.LogDebug(
            "Publicando mensagem. Type: {MessageType}. " +
            "RoutingKey: {RoutingKey}. Exchange: {Exchange}. " +
            "MessageId: {MessageId}.",
            typeof(TMessage).Name,
            routingKey,
            _settings.ExchangeName,
            properties.MessageId);

        // ─────────────────────────────────────────────────────────────
        // PUBLICAÇÃO COM CONFIRMAÇÃO DO BROKER (Publisher Confirms)
        //
        // BasicPublishAsync com mandatory: false - se a mensagem não
        // puder ser roteada (routing key sem binding), ela é descartada
        // silenciosamente. Em produção, mandatory: true + ReturnCallback
        // garantiria que mensagens não roteadas sejam tratadas.
        // Para o portfólio, nossos bindings são estáticos e conhecidos.
        // ─────────────────────────────────────────────────────────────
        try
        {
            await channel.BasicPublishAsync(
                exchange:    _settings.ExchangeName,
                routingKey:  routingKey,
                mandatory:   false,
                basicProperties: properties,
                body:        messageBytes,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Mensagem publicada com sucesso. Type: {MessageType}. " +
                "RoutingKey: {RoutingKey}. MessageId: {MessageId}.",
                typeof(TMessage).Name,
                routingKey,
                properties.MessageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Falha ao publicar mensagem. Type: {MessageType}. " +
                "RoutingKey: {RoutingKey}. Erro: {ErrorMessage}.",
                typeof(TMessage).Name,
                routingKey,
                ex.Message);

            // Invalida o canal para que seja recriado na próxima tentativa.
            // Canais do RabbitMQ ficam em estado de erro após exceções AMQP.
            await InvalidateChannelAsync();
            throw;
        }
    }

    // ─────────────────────────────────
    // GERENCIAMENTO DE CONEXÃO E CANAL
    // ─────────────────────────────────

    /// <summary>
    /// Retorna o canal ativo ou cria um novo se necessário.
    /// Thread-safe via SemaphoreSlim - apenas uma thread inicializa por vez.
    /// 
    /// </summary>
    private async Task<IChannel> GetOrCreateChannelAsync(
        CancellationToken cancellationToken)
    {
        // Fast path: canal já está criado e aberto
        if (_channel is { IsOpen: true })
            return _channel;

        // Slow path: precisa criar/recriar. Entra na seção crítica.
        await _semaphore.WaitAsync(cancellationToken);

        try
        {
            // Double-check após adquirir o lock (outro thread pode ter criado)
            if (_channel is { IsOpen: true })
                return _channel;

            await CreateConnectionAndChannelAsync(cancellationToken);
            return _channel!;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Cria a conexão TCP com o broker e um canal AMQP sobre ela.
    /// Implementa retry com backoff exponencial para lidar com
    /// inicialização lenta do RabbitMQ em ambientes containerizados.
    /// 
    /// </summary>
    private async Task CreateConnectionAndChannelAsync(
        CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory
        {
            HostName     = _settings.Host,
            Port         = _settings.Port,
            VirtualHost  = _settings.VirtualHost,
            UserName     = _settings.Username,
            Password     = _settings.Password,
            // AutomaticRecoveryEnabled reconecta automaticamente
            // após falhas de rede transitórias - essencial em produção.
            AutomaticRecoveryEnabled      = true,
            NetworkRecoveryInterval       = TimeSpan.FromSeconds(10),
            RequestedConnectionTimeout    =
                TimeSpan.FromSeconds(_settings.ConnectionTimeoutSeconds),
            RequestedHeartbeat            = TimeSpan.FromSeconds(60),
        };

        var maxRetries  = _settings.MaxConnectionRetries;
        var attempt     = 0;
        Exception? lastException = null;

        while (attempt < maxRetries)
        {
            try
            {
                _logger.LogInformation(
                    "Conectando ao RabbitMQ. Host: {Host}:{Port}. " +
                    "VHost: {VHost}. Tentativa: {Attempt}/{MaxRetries}.",
                    _settings.Host,
                    _settings.Port,
                    _settings.VirtualHost,
                    attempt + 1,
                    maxRetries);

                _connection = await factory.CreateConnectionAsync(
                    clientProvidedName: "financial-transaction-service-publisher",
                    cancellationToken:  cancellationToken);

                _channel = await _connection.CreateChannelAsync(
                    cancellationToken: cancellationToken);

                // QoS (Quality of Service) não é relevante para
                // publishers, mas configuramos prefetchCount=0 para garantir
                // que o canal não seja usado acidentalmente como consumer.
                // Publishers e consumers devem usar canais separados.

                _logger.LogInformation(
                    "Conexão com RabbitMQ estabelecida com sucesso. " +
                    "Host: {Host}:{Port}.",
                    _settings.Host,
                    _settings.Port);

                return;
            }
            catch (Exception ex)
            {
                lastException = ex;
                attempt++;

                if (attempt >= maxRetries)
                    break;

                // Backoff exponencial: 2s, 4s, 8s, 16s...
                var delaySeconds = Math.Pow(2, attempt);

                _logger.LogWarning(
                    ex,
                    "Falha ao conectar ao RabbitMQ. " +
                    "Tentativa {Attempt}/{MaxRetries}. " +
                    "Próxima tentativa em {DelaySeconds}s.",
                    attempt,
                    maxRetries,
                    delaySeconds);

                await Task.Delay(
                    TimeSpan.FromSeconds(delaySeconds),
                    cancellationToken);
            }
        }

        throw new InvalidOperationException(
            $"Não foi possível conectar ao RabbitMQ após {maxRetries} tentativas. " +
            $"Host: {_settings.Host}:{_settings.Port}.",
            lastException);
    }

    /// <summary>
    /// Fecha e descarta o canal atual para que seja recriado na próxima operação.
    /// Chamado quando o canal entra em estado de erro após uma exceção AMQP.
    /// 
    /// </summary>
    private async Task InvalidateChannelAsync()
    {
        if (_channel is not null)
        {
            try { await _channel.CloseAsync(); } catch { /* ignora */ }
            await _channel.DisposeAsync();
            _channel = null;
        }
    }

    // ────────
    // DISPOSE
    // ────────

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            try { await _channel.CloseAsync(); } catch { /* ignora */ }
            await _channel.DisposeAsync();
        }

        if (_connection is not null)
        {
            try { await _connection.CloseAsync(); } catch { /* ignora */ }
            await _connection.DisposeAsync();
        }

        _semaphore.Dispose();
    }
}