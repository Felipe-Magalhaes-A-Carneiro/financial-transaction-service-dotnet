using FinancialTransactionService.Domain.Entities;
using FinancialTransactionService.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialTransactionService.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuração Fluent API do mapeamento da entidade Transaction para o banco.
///
/// Responsabilidades desta classe:
/// - Define o nome da tabela e schema no PostgreSQL
/// - Mapeia cada propriedade C# para sua coluna SQL com tipo, constraints e defaults
/// - Configura Value Conversions para enums (armazenados como string para legibilidade)
/// - Define índices para as queries mais frequentes (performance crítica)
/// - Configura os relacionamentos com Account (FK sem cascade delete)
///
/// Convenção de nomenclatura: snake_case para tabelas e colunas no PostgreSQL
/// (padrão da comunidade Postgres), PascalCase para propriedades em C#.
/// </summary>
public sealed class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        // ───────
        // TABELA
        // ───────
        builder.ToTable("transactions");

        // ───────────────
        // CHAVE PRIMÁRIA
        // ───────────────
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            // uuid_generate_v4() é a função do PostgreSQL que gera UUIDs.
            // Habilitada pelo init.sql com CREATE EXTENSION "uuid-ossp".
            // HasDefaultValueSql garante que o banco gera o UUID se C# não fornecer,
            // mas nosso BaseEntity sempre fornece via Guid.NewGuid() no construtor.
            .HasDefaultValueSql("uuid_generate_v4()");

        // ─────────────────────────────────
        // CAMPOS DE AUDITORIA (BaseEntity)
        // ─────────────────────────────────
        builder.Property(t => t.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")  // timestamp with time zone - sempre UTC
            .IsRequired()
            .HasDefaultValueSql("NOW()");

        builder.Property(t => t.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamptz")
            .IsRequired()
            .HasDefaultValueSql("NOW()");

        // ─────────────────────────────────────────────────────
        // CONTAS (FKs opcionais - dependem do TransactionType)
        // ─────────────────────────────────────────────────────
        builder.Property(t => t.SourceAccountId)
            .HasColumnName("source_account_id")
            .HasColumnType("uuid")
            .IsRequired(false);  // null para Credit e Refund

        builder.Property(t => t.DestinationAccountId)
            .HasColumnName("destination_account_id")
            .HasColumnType("uuid")
            .IsRequired(false);  // null para Debit e Payment

        // ─────────────────
        // VALOR FINANCEIRO
        // ─────────────────
        builder.Property(t => t.AmountInCents)
            .HasColumnName("amount_in_cents")
            .HasColumnType("bigint")  // long em C# -> bigint em PostgreSQL
            .IsRequired();

        builder.Property(t => t.Currency)
            .HasColumnName("currency")
            .HasColumnType("char(3)")  // ISO 4217: sempre 3 caracteres
            .IsRequired()
            .HasMaxLength(3);

        // ─────────────────────────────────────────────────────────────
        // ENUMS — Value Conversion para string
        //
        // Armazenar enums como string (ex: "Transfer", "Completed") em vez
        // de int (1, 3) tem vantagens críticas em sistemas financeiros:
        // - Legibilidade direta no banco sem precisar de dicionário
        // - Relatórios SQL e queries ad-hoc são auto-explicativas
        // - Adicionar novos valores ao enum não impacta dados históricos
        // - Auditoria humana facilitada (compliance e regulatório)
        // ─────────────────────────────────────────────────────────────
        builder.Property(t => t.Type)
            .HasColumnName("type")
            .HasColumnType("varchar(20)")
            .IsRequired()
            .HasConversion<string>();

        builder.Property(t => t.Status)
            .HasColumnName("status")
            .HasColumnType("varchar(20)")
            .IsRequired()
            .HasConversion<string>();

        // ────────────────
        // CAMPOS TEXTUAIS
        // ────────────────
        builder.Property(t => t.Description)
            .HasColumnName("description")
            .HasColumnType("varchar(500)")
            .IsRequired(false)
            .HasMaxLength(500);

        builder.Property(t => t.FailureReason)
            .HasColumnName("failure_reason")
            .HasColumnType("text")
            .IsRequired(false);

        // ───────────────────────────────
        // IDEMPOTÊNCIA E RASTREABILIDADE
        // ───────────────────────────────
        builder.Property(t => t.CorrelationId)
            .HasColumnName("correlation_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(t => t.ProcessedAt)
            .HasColumnName("processed_at")
            .HasColumnType("timestamptz")
            .IsRequired(false);

        // ─────────────────────────────────────────────────────────────
        // RELACIONAMENTOS
        // Configurados com Restrict (sem cascade delete) por segurança:
        // nunca deletamos uma conta e perdemos o histórico de transações.
        // ─────────────────────────────────────────────────────────────
        builder.HasOne(t => t.SourceAccount)
            .WithMany(a => a.Transactions)
            .HasForeignKey(t => t.SourceAccountId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        // HasOne sem WithMany para DestinationAccount porque
        // Account.Transactions já representa as transações de origem.
        // Para manter a coleção simples, não duplicamos a navegação.
        // Em um sistema real com relatórios complexos, poderíamos
        // adicionar Account.IncomingTransactions e Account.OutgoingTransactions.
        builder.HasOne(t => t.DestinationAccount)
            .WithMany()
            .HasForeignKey(t => t.DestinationAccountId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        // ─────────────────────────────────────────────────────────────
        // ÍNDICES - críticos para performance em produção
        //
        // Regra: todo campo usado em WHERE, ORDER BY ou JOIN frequente
        // deve ter índice. Sem índice = full table scan em produção.
        // ─────────────────────────────────────────────────────────────

        // Índice único em CorrelationId: garante idempotência no nível do banco
        // (segunda linha de defesa, além do Redis). Também acelera o lookup
        // GetByCorrelationIdAsync().
        builder.HasIndex(t => t.CorrelationId)
            .IsUnique()
            .HasDatabaseName("ix_transactions_correlation_id");

        // Índice em SourceAccountId para GetByAccountIdAsync()
        builder.HasIndex(t => t.SourceAccountId)
            .HasDatabaseName("ix_transactions_source_account_id");

        // Índice em DestinationAccountId para GetByAccountIdAsync()
        builder.HasIndex(t => t.DestinationAccountId)
            .HasDatabaseName("ix_transactions_destination_account_id");

        // Índice em Status para GetByStatusAndDateRangeAsync() e monitoramento
        builder.HasIndex(t => t.Status)
            .HasDatabaseName("ix_transactions_status");

        // Índice composto em (Status, CreatedAt) para queries de monitoramento
        // que filtram por status E ordenam por data - cobre ambos os predicados
        // em uma única estrutura de índice, mais eficiente que dois índices separados.
        builder.HasIndex(t => new { t.Status, t.CreatedAt })
            .HasDatabaseName("ix_transactions_status_created_at");

        // ─────────────────────────────────────────────────────────────
        // IGNORAR MEMBROS NÃO MAPEADOS
        // DomainEvents é uma coleção em memória - não deve ser persistida.
        // ─────────────────────────────────────────────────────────────
        builder.Ignore(t => t.DomainEvents);
    }
}