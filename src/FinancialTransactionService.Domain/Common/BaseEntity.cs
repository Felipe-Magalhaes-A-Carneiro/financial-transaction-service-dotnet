using System.Runtime.CompilerServices;

namespace FinancialTransactionService.Domain.Common;

/// <summary>
/// Classe base para todas as entidades de domínio.
/// Centraliza os campos de auditoria obrigatórios em sistemas financeiros
/// e o identificador único, eliminando repetição de código entre entidades.
/// 
/// </summary>


public abstract class BaseEntity
{
    /// <summary> Identificador único da entidade (UUID v4). </summary>
    public Guid Id {get; private set; }

    /// <summary> Timestamp UTC da criação do registro. Imutável após criação.</summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary> Timestamp UTC da última modificação. Atualizado em toda mudança de estado.</summary>
    public DateTime UpdatedAt { get; private set; }    

    /// A coleção de eventos é privada para garantir que apenas a própria entridade adicione eventos a ela, nunca por um código externo. Será identificada como IReadOnlyCollection para leitura pelo dispatcher 
    private readonly List<IDomainEvent> _domainEvents = [];

    /// <summary>
    /// Eventos de domínio gerados por esta entidade, ainda não publicados. Serão coletados e despachados pelo Unit of Work ao fazer SaveChanges
    /// </summary>
    public IReadOnlyCollection<IDomainEvent> DomaingEvents => _domainEvents.AsReadOnly();


    protected BaseEntity()
    {
        Id = Guid.NewGuid();
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Registra um novo evento de domínio na entidade.
    /// O Evento será publicado pelo Unit of Work após a persistência bem-sucedida, garantindo consistência: o evento só sai se o dado foi salvo
    /// </summary>
    /// <param name="domainEvent"> O evento a ser registrado.</param>
    protected void AddDomainEvent(IDomainEvent domaingEvent)
    {
        _domainEvents.Add(domaingEvent);
    }

    /// <summary>
    /// Remove todos os eventos após a publicação pelo dispatcher.
    /// Chamado pelo Unit od Work depois de despachar os eventos com sucesso.
    /// </summary>
    public void ClearDomainEvents()
    {
        _domainEvents.Clear();
    }

    /// <summary>
    /// Atualiza o timestamp de modificação.
    /// Deve ser chamado por métodos de domínio sempre que o estado da entidade mudar.
    /// </summary>
    protected void SetUpdatedAt()
    {
        UpdatedAt = DateTime.UtcNow;
    }

}