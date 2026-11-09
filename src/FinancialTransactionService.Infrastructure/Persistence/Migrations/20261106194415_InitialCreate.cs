using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialTransactionService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    account_number = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    holder_name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    balance_in_cents = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "transactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    source_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    destination_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amount_in_cents = table.Column<long>(type: "bigint", nullable: false),
                    currency = table.Column<string>(type: "char(3)", maxLength: 3, nullable: false),
                    type = table.Column<string>(type: "varchar(20)", nullable: false),
                    status = table.Column<string>(type: "varchar(20)", nullable: false),
                    description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    failure_reason = table.Column<string>(type: "text", nullable: true),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    processed_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transactions", x => x.id);
                    table.ForeignKey(
                        name: "FK_transactions_accounts_destination_account_id",
                        column: x => x.destination_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_transactions_accounts_source_account_id",
                        column: x => x.source_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_accounts_account_number",
                table: "accounts",
                column: "account_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_accounts_is_active",
                table: "accounts",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_transactions_correlation_id",
                table: "transactions",
                column: "correlation_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_transactions_destination_account_id",
                table: "transactions",
                column: "destination_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_transactions_source_account_id",
                table: "transactions",
                column: "source_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_transactions_status",
                table: "transactions",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_transactions_status_created_at",
                table: "transactions",
                columns: new[] { "status", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "transactions");

            migrationBuilder.DropTable(
                name: "accounts");
        }
    }
}
