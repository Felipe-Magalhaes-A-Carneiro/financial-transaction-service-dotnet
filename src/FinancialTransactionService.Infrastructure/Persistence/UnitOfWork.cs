using FinancialTransactionService.Application.Common.Interfaces;
using FinancialTransactionService.Domain.Common;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using FinancialTransactionService.Domain.Interfaces;

namespace FinancialTransactionService.Infrastructure.Persistence;

/// <summary>
/// Implementação do padrão Unit of Work.
///
/// Responsabilidades centrais:
/// 1- Coordena o SaveChanges do EF Core como ponto único de persistência.
/// 2- Atualiza timestamps de auditoria automaticamente (via DbContext).
/// 3- Coleta e despacha Domain Events das entidades modificadas
///    APÓS o commit bem-sucedido, garantindo consistência entre
///    banco de dados e broker de mensagens.
/// 4- Gerencia transações de banco de dados explícitas para operações
///    que precisam de atomicidade entre múltiplas entidades (ex: transferência).
/// 
/// </summary>
public sealed class UnitOfWork : IUnitOfWork
{
    private readonly ApplicationDbContext _context;
    private readonly IMessagePublisher _messagePublisher;
    private readonly ILogger<UnitOfWork> _logger;

    // Transação de banco de dados explícita (opcional).
    // Null quando não há transação explícita em andamento, 
    // SaveChanges usa a transação implícita do EF Core neste caso.
    private IDbContextTransaction? _currentTransaction;

    public UnitOfWork(
        ApplicationDbContext context,
        IMessagePublisher messagePublisher,
        ILogger<UnitOfWork> logger)
    {
        _context          = context;
        _messagePublisher = messagePublisher;
        _logger           = logger;
    }

    // ───────────────────────────────────
    // PERSISTÊNCIA E DESPACHO DE EVENTOS
    // ───────────────────────────────────

    /// <summary>
    /// Persiste todas as alterações rastreadas pelo EF Core e despacha
    /// os Domain Events das entidades modificadas após o commit.
    ///
    /// Ordem de execução garantida:
    ///   1- Coleta todos os Domain Events das entidades rastreadas
    ///   2- Executa base.SaveChangesAsync(), persiste no banco
    ///   3- Limpa os eventos das entidades (evita despacho duplo)
    ///   4- Despacha cada evento via IMessagePublisher
    ///
    /// A coleta de eventos acontece ANTES do SaveChanges porque o
    /// EF Core pode alterar o estado das entidades durante a persistência
    /// (ex: gerar IDs, aplicar defaults). Os eventos já foram registrados
    /// nos construtores/métodos das entidades com os dados corretos.
    /// </summary>
    public async Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        // 1- Coleta Domain Events de TODAS as entidades rastreadas
        //    que herdam de BaseEntity e têm eventos pendentes.
        var entitiesWithEvents = _context.ChangeTracker
            .Entries<BaseEntity>()
            .Where(e => e.Entity.DomainEvents.Any())
            .Select(e => e.Entity)
            .ToList();

        var domainEvents = entitiesWithEvents
            .SelectMany(e => e.DomainEvents)
            .ToList();

        _logger.LogDebug(
            "SaveChanges: {EntityCount} entidade(s) com {EventCount} Domain Event(s) pendente(s).",
            entitiesWithEvents.Count,
            domainEvents.Count);

        // 2- Persiste no banco de dados
        var result = await _context.SaveChangesAsync(cancellationToken);

        // 3- Limpa os eventos APÓS o SaveChanges bem-sucedido.
        //    Se SaveChanges lançar exceção, os eventos são preservados
        //    nas entidades, o caller (handler) pode tratar e tentar novamente.
        foreach (var entity in entitiesWithEvents)
            entity.ClearDomainEvents();

        // 4- Despacha os eventos coletados via IMessagePublisher
        //    Executado SOMENTE após o commit bem-sucedido.
        if (domainEvents.Count > 0)
            await DispatchDomainEventsAsync(domainEvents, cancellationToken);

        return result;
    }

    // ───────────────────────────────────────
    // GERENCIAMENTO DE TRANSAÇÕES EXPLÍCITAS
    // ───────────────────────────────────────

    /// <inheritdoc/>
    public async Task BeginTransactionAsync(
        CancellationToken cancellationToken = default)
    {
        if (_currentTransaction is not null)
        {
            // Em vez de lançar exceção, logamos e retornamos.
            // Algumas operações chamam BeginTransaction por precaução mesmo
            // quando já existe uma transação ativa, comportamento seguro.
            _logger.LogWarning(
                "Tentativa de iniciar transação quando já existe uma ativa. " +
                "A transação atual será reutilizada.");
            return;
        }

        _currentTransaction = await _context.Database
            .BeginTransactionAsync(cancellationToken);

        _logger.LogDebug(
            "Transação de banco de dados iniciada. " +
            "TransactionId: {TransactionId}.",
            _currentTransaction.TransactionId);
    }

    /// <inheritdoc/>
    public async Task CommitTransactionAsync(
        CancellationToken cancellationToken = default)
    {
        if (_currentTransaction is null)
        {
            _logger.LogWarning(
                "Tentativa de commit sem transação ativa. Operação ignorada.");
            return;
        }

        try
        {
            await _currentTransaction.CommitAsync(cancellationToken);

            _logger.LogDebug(
                "Transação comitada com sucesso. " +
                "TransactionId: {TransactionId}.",
                _currentTransaction.TransactionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Falha ao comitar transação. " +
                "TransactionId: {TransactionId}.",
                _currentTransaction.TransactionId);
            throw;
        }
        finally
        {
            // Sempre limpamos a referência após commit ou falha,
            // independente do resultado. O finally garante que o estado
            // interno do UoW seja consistente mesmo em caso de exceção,
            // evitando que uma transação "fantasma" bloqueie operações futuras.
            await _currentTransaction.DisposeAsync();
            _currentTransaction = null;
        }
    }

    /// <inheritdoc/>
    public async Task RollbackTransactionAsync(
        CancellationToken cancellationToken = default)
    {
        if (_currentTransaction is null)
        {
            _logger.LogWarning(
                "Tentativa de rollback sem transação ativa. Operação ignorada.");
            return;
        }

        try
        {
            await _currentTransaction.RollbackAsync(cancellationToken);

            _logger.LogDebug(
                "Rollback executado. " +
                "TransactionId: {TransactionId}.",
                _currentTransaction.TransactionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Falha ao executar rollback. " +
                "TransactionId: {TransactionId}.",
                _currentTransaction.TransactionId);
            throw;
        }
        finally
        {
            await _currentTransaction.DisposeAsync();
            _currentTransaction = null;
        }
    }

    // ──────────────────────────
    // DESPACHO DE DOMAIN EVENTS
    // ──────────────────────────

    /// <summary>
    /// Despacha cada Domain Event coletado via IMessagePublisher.
    ///
    /// Estratégia de roteamento por tipo de evento:
    /// Cada tipo de evento tem sua routing key específica no RabbitMQ,
    /// permitindo que consumers diferentes se inscrevam em eventos diferentes
    /// sem acoplamento entre si.
    ///
    /// Ordem de despacho: na mesma ordem em que os eventos foram registrados
    /// nas entidades, preserva a sequência causal dos fatos do domínio.
    /// 
    /// </summary>
    private async Task DispatchDomainEventsAsync(
        IReadOnlyList<IDomainEvent> domainEvents,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Despachando {Count} Domain Event(s).",
            domainEvents.Count);

        foreach (var domainEvent in domainEvents)
        {
            try
            {
                // O roteamento por tipo de evento mapeia cada
                // Domain Event para sua routing key no RabbitMQ.
                // A implementação concreta do IMessagePublisher (futuramente)
                // usará esta routing key para publicar na Exchange correta.
                var routingKey = GetRoutingKey(domainEvent);

                if (routingKey is null)
                {
                    _logger.LogDebug(
                        "Domain Event sem routing key configurada. " +
                        "EventType: {EventType}. Ignorado.",
                        domainEvent.GetType().Name);
                    continue;
                }

                // Publicamos o evento diretamente como mensagem no broker.
                // O consumer do RabbitMQ (futuramente) receberá e processará.
                await _messagePublisher.PublishAsync(
                    message:    domainEvent,
                    routingKey: routingKey,
                    headers: new Dictionary<string, object>
                    {
                        ["eventId"]   = domainEvent.EventId.ToString(),
                        ["eventType"] = domainEvent.GetType().Name,
                        ["occurredOn"] = domainEvent.OccurredOn.ToString("O")
                    },
                    cancellationToken: cancellationToken);

                _logger.LogDebug(
                    "Domain Event despachado. " +
                    "EventType: {EventType}. EventId: {EventId}. " +
                    "RoutingKey: {RoutingKey}.",
                    domainEvent.GetType().Name,
                    domainEvent.EventId,
                    routingKey);
            }
            catch (Exception ex)
            {
                // Logamos o erro mas NÃO re-lançamos.
                // O dado já foi salvo no banco, não queremos que uma falha
                // de publicação de evento desfaça o commit bem-sucedido.
                // Em produção, um Outbox Pattern completo com tabela de eventos
                // garantiria a entrega eventual. Para o portfólio, logamos
                // o evento perdido para que o time possa investigar e reprocessar.
                _logger.LogError(
                    ex,
                    "Falha ao despachar Domain Event. " +
                    "EventType: {EventType}. EventId: {EventId}. " +
                    "O dado foi salvo mas o evento pode ter sido perdido.",
                    domainEvent.GetType().Name,
                    domainEvent.EventId);
            }
        }
    }

    /// <summary>
    /// Mapeia um Domain Event para sua routing key no RabbitMQ.
    /// Retorna null para eventos que não devem ser publicados no broker.
    /// 
    /// </summary>
    private static string? GetRoutingKey(IDomainEvent domainEvent) =>
        domainEvent.GetType().Name switch
        {
            // Usamos o nome do tipo como chave do switch para
            // evitar dependência de assembly entre Domain e Infrastructure.
            // Routing keys devem coincidir com os bindings configurados
            // no definitions.json do RabbitMQ (já implementado).
            "TransactionCreatedEvent"   => "transactions.created",
            "TransactionCompletedEvent" => "transactions.completed",
            "TransactionFailedEvent"    => "transactions.failed",

            // Eventos sem routing key são ignorados silenciosamente.
            _ => null
        };

    // ────────
    // DISPOSE
    // ────────

    public void Dispose()
    {
        _currentTransaction?.Dispose();
        _context.Dispose();
    }
}