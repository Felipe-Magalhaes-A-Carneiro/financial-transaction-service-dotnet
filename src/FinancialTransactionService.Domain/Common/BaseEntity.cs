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


    // O seguinte construtor protegido serve a dois propósitos:
    // 1. Permite que classes filhas o chamem via "base()".
    // 2. Impede instanciação direta de BaseEntity (que é abstrata de qualquer forma).
    // O EF Core consegue chamar este construtor via reflexão para materializar entidades do banco.

    protected BaseEntity()
    {
        Id = Guid.NewGuid();
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
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