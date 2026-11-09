using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace FinancialTransactionService.Infrastructure.Persistence;

/// <summary>
/// Factory usada exclusivamente pelo EF Core CLI em tempo de design.
/// Esta classe NÃO é registrada no container de DI e NÃO é usada em runtime.
/// </summary>
public sealed class ApplicationDbContextFactory
    : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        // Tentamos múltiplos caminhos porque o diretório atual
        // varia conforme de onde o "dotnet ef" é executado:
        // - Da raiz do repositório: cwd = /repo-root
        // - Do projeto Infrastructure: cwd = /repo-root/src/Infrastructure
        // A lista abaixo cobre os cenários mais comuns.
        var possibleBasePaths = new[]
        {
            // Executado da raiz do repositório (caso mais comum)
            Path.Combine(Directory.GetCurrentDirectory(),
                "src", "FinancialTransactionService.Api"),

            // Executado de dentro do projeto Infrastructure
            Path.Combine(Directory.GetCurrentDirectory(),
                "..", "FinancialTransactionService.Api"),

            // Executado de dentro de src/
            Path.Combine(Directory.GetCurrentDirectory(),
                "FinancialTransactionService.Api"),
        };

        string? basePath = possibleBasePaths
            .FirstOrDefault(p => File.Exists(
                Path.Combine(p, "appsettings.json")));

        string? connectionString = null;

        if (basePath is not null)
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(basePath)
                .AddJsonFile("appsettings.json", optional: false)
                .AddJsonFile("appsettings.Development.json", optional: true)
                .AddEnvironmentVariables()
                .Build();

            connectionString = configuration
                .GetConnectionString("DefaultConnection");
        }

        // Fallback para connection string hardcoded de design-time.
        // Usado APENAS pelo EF Core CLI para gerar/aplicar migrations.
        // Nunca é usado em runtime — o DI do ASP.NET Core injeta
        // as configurações reais via variáveis de ambiente.
        connectionString ??=
            "Host=localhost;Port=5432;" +
            "Database=financial_transactions;" +
            "Username=fts_user;" +
            "Password=fts_secret_password;";

        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();

        optionsBuilder.UseNpgsql(
            connectionString,
            npgsqlOptions =>
            {
                npgsqlOptions.MigrationsAssembly(
                    typeof(ApplicationDbContext).Assembly.FullName);
            });

        return new ApplicationDbContext(optionsBuilder.Options);
    }
}