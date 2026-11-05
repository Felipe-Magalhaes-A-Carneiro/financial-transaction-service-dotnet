using FinancialTransactionService.Application.Common.Interfaces;
using FinancialTransactionService.Domain.Common;
using FinancialTransactionService.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinancialTransactionService.Infrastructure.Persistence;

/// <summary>
/// DbContext central da aplicação. Implementa IApplicationDbContext para
/// expor DbSets às queries de leitura da camada Application.
///
/// Responsabilidades:
/// - Registra todas as entidades e aplica suas configurações Fluent API
/// - Implementa a interface IApplicationDbContext (contrato da Application)
/// - Configura convenções globais de mapeamento (snake_case, timestamptz)
/// - Sobrescreve SaveChangesAsync para atualizar timestamps de auditoria
///   automaticamente em toda mudança de estado de entidade
///
/// O que NÃO está aqui (separação de responsabilidades):
/// - Lógica de negócio → Domain e Application
/// - Publicação de Domain Events → UnitOfWork (Passo 4.3)
/// - Gestão de transações → UnitOfWork (Passo 4.3)
/// </summary>
public sealed class ApplicationDbContext : DbContext, IApplicationDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    // ─────────────────────────────────────────────────────────────────────
    // DbSets — expostos como propriedades para IApplicationDbContext
    // ─────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public DbSet<Transaction> Transactions => Set<Transaction>();

    /// <inheritdoc/>
    public DbSet<Account> Accounts => Set<Account>();

    // ─────────────────────────────────────────────────────────────────────
    // MODELO
    // ─────────────────────────────────────────────────────────────────────

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // IA diz: ApplyConfigurationsFromAssembly() descobre e aplica automaticamente
        // todas as classes que implementam IEntityTypeConfiguration<T> neste assembly.
        // Adicionar uma nova entidade = criar sua Configuration class.
        // Zero modificação necessária neste método.
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(ApplicationDbContext).Assembly);

        // ─────────────────────────────────────────────────────────────
        // CONVENÇÕES GLOBAIS
        // ─────────────────────────────────────────────────────────────

        // Configura DateTime para sempre ser tratado como UTC no Npgsql.
        // Essencial para evitar bugs de fuso horário em sistemas financeiros
        // onde timestamps de transação devem ser inequivocamente em UTC.
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", false);
    }

    // ─────────────────────────────────────────────────────────────────────
    // AUDITORIA AUTOMÁTICA
    // Sobrescrevemos SaveChangesAsync para interceptar toda operação de
    // escrita e atualizar os campos de auditoria automaticamente.
    // O desenvolvedor nunca precisa lembrar de chamar SetUpdatedAt() —
    // o DbContext garante isso para todas as entidades rastreadas.
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Persiste as alterações no banco de dados, atualizando automaticamente
    /// os campos de auditoria (CreatedAt, UpdatedAt) de todas as entidades
    /// modificadas rastreadas pelo Change Tracker.
    /// </summary>
    public override async Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        UpdateAuditFields();
        return await base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Atualiza CreatedAt e UpdatedAt baseado no estado de cada entidade
    /// rastreada pelo Change Tracker antes do SaveChanges.
    /// </summary>
    private void UpdateAuditFields()
    {
        var utcNow = DateTime.UtcNow;

        // IA diz: ChangeTracker.Entries<BaseEntity>() filtra apenas as
        // entidades que herdam de BaseEntity (nossas entidades de domínio),
        // ignorando entidades internas do EF Core como shadow properties.
        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    // IA diz: Para entidades novas, SetProperty via reflection
                    // permite sobrescrever a propriedade privada que o construtor
                    // já setou. Isso garante que CreatedAt seja o momento exato
                    // do SaveChanges (não da instanciação do objeto em memória),
                    // evitando discrepâncias em cenários de alta latência.
                    entry.Property(nameof(BaseEntity.CreatedAt)).CurrentValue = utcNow;
                    entry.Property(nameof(BaseEntity.UpdatedAt)).CurrentValue = utcNow;
                    break;

                case EntityState.Modified:
                    // Para entidades modificadas, apenas UpdatedAt muda.
                    // CreatedAt é imutável após a criação.
                    entry.Property(nameof(BaseEntity.CreatedAt)).IsModified = false;
                    entry.Property(nameof(BaseEntity.UpdatedAt)).CurrentValue = utcNow;
                    break;

                case EntityState.Unchanged:
                case EntityState.Deleted:
                case EntityState.Detached:
                default:
                    // IA diz: Soft delete não está no escopo deste portfólio,
                    // mas em sistemas financeiros reais, Deleted raramente
                    // é usado — entidades financeiras são imutáveis por lei.
                    break;
            }
        }
    }
}