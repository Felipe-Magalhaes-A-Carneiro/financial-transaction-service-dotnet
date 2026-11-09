using FinancialTransactionService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialTransactionService.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuração Fluent API do mapeamento da entidade Account para o banco.
///
/// Decisão de design sobre saldo:
/// Armazenamos BalanceInCents como bigint (long) - sem ponto flutuante.
/// Um saldo de R$ 1.000.000.000,00 (1 bilhão de reais) = 100_000_000_000 centavos.
/// O tipo bigint suporta até ~9,2 * 10^18 - mais que suficiente.
/// decimal(18,2) seria a alternativa, mas long com centavos é mais
/// performático para operações aritméticas de alta frequência.
/// 
/// </summary>
public sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        // ───────
        // TABELA
        // ───────
        builder.ToTable("accounts");

        // ───────────────
        // CHAVE PRIMÁRIA
        // ───────────────
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .HasDefaultValueSql("uuid_generate_v4()");

        // ─────────────────────────────────
        // CAMPOS DE AUDITORIA (BaseEntity)
        // ─────────────────────────────────
        builder.Property(a => a.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired()
            .HasDefaultValueSql("NOW()");

        builder.Property(a => a.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamptz")
            .IsRequired()
            .HasDefaultValueSql("NOW()");

        // ───────────────
        // DADOS DA CONTA
        // ───────────────
        builder.Property(a => a.AccountNumber)
            .HasColumnName("account_number")
            .HasColumnType("varchar(20)")
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(a => a.HolderName)
            .HasColumnName("holder_name")
            .HasColumnType("varchar(200)")
            .IsRequired()
            .HasMaxLength(200);

        // ─────────────────
        // SALDO FINANCEIRO
        // ─────────────────
        builder.Property(a => a.BalanceInCents)
            .HasColumnName("balance_in_cents")
            .HasColumnType("bigint")
            .IsRequired()
            .HasDefaultValue(0L);

        // ───────
        // STATUS
        // ───────
        builder.Property(a => a.IsActive)
            .HasColumnName("is_active")
            .HasColumnType("boolean")
            .IsRequired()
            .HasDefaultValue(true);

        // ───────
        // ÍNDICES
        // ───────
        // Índice único em AccountNumber: garante unicidade de número de conta
        // no nível do banco (segunda linha de defesa após a verificação no handler).
        builder.HasIndex(a => a.AccountNumber)
            .IsUnique()
            .HasDatabaseName("ix_accounts_account_number");

        // Índice em IsActive: queries de filtragem por contas ativas
        // são muito frequentes em sistemas financeiros.
        builder.HasIndex(a => a.IsActive)
            .HasDatabaseName("ix_accounts_is_active");

        // ────────
        // IGNORAR
        // ────────
        builder.Ignore(a => a.DomainEvents);
    }
}