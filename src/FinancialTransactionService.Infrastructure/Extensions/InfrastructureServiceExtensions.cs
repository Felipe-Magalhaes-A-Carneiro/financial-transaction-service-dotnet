using FinancialTransactionService.Application.Common.Interfaces;
using FinancialTransactionService.Domain.Interfaces;
using FinancialTransactionService.Infrastructure.Persistence;
using FinancialTransactionService.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialTransactionService.Infrastructure.Extensions;

/// <summary>
/// Extensões para registro dos serviços da camada Infrastructure no container de DI.
///
/// Encapsula todos os detalhes de infraestrutura (EF Core, repositórios,
/// Unit of Work) em um único método de extensão.
/// O Program.cs (será implementado) chamará apenas:
///   builder.Services.AddInfrastructureServices(builder.Configuration);
///
/// Os serviços de Messaging (RabbitMQ) e Caching (Redis) serão
/// registrados aqui nos Capítulos 5 e 6 respectivamente.
/// </summary>
public static class InfrastructureServiceExtensions
{
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ─────────────────────
        // EF CORE + POSTGRESQL
        // ─────────────────────
        services.AddDbContext<ApplicationDbContext>(options =>
        {
            var connectionString = configuration
                .GetConnectionString("DefaultConnection");

            options.UseNpgsql(
                connectionString,
                npgsqlOptions =>
                {
                    npgsqlOptions.MigrationsAssembly(
                        typeof(ApplicationDbContext).Assembly.FullName);

                    // CommandTimeout de 30s para operações normais.
                    // Em produção, operações de longa duração (relatórios,
                    // reconciliação) deveriam ter seus próprios DbContexts
                    // com timeout configurado individualmente.
                    npgsqlOptions.CommandTimeout(30);

                    // Estratégia de retry para falhas transitórias de conexão.
                    // Essencial em ambientes containerizados onde o banco
                    // pode estar inicializando quando a API sobe.
                    npgsqlOptions.EnableRetryOnFailure(
                        maxRetryCount:       3,
                        maxRetryDelay:       TimeSpan.FromSeconds(5),
                        errorCodesToAdd:     null);
                });

            // Em desenvolvimento, habilitamos logs detalhados do EF Core
            // para visualizar o SQL gerado e detectar N+1 facilmente.
            // Em produção (ASPNETCORE_ENVIRONMENT=Production), este bloco
            // é ignorado pelo runtime.
#if DEBUG
            options.EnableSensitiveDataLogging();
            options.EnableDetailedErrors();
#endif
        });

        // ─────────────────────────────────────────────────────────────
        // IApplicationDbContext
        // Registramos ApplicationDbContext como IApplicationDbContext
        // para os QueryHandlers da Application que precisam de IQueryable.
        // Scoped: um DbContext por requisição HTTP, padrão correto.
        // ─────────────────────────────────────────────────────────────
        services.AddScoped<IApplicationDbContext>(
            provider => provider.GetRequiredService<ApplicationDbContext>());

        // ─────────────────────────────────────────────────────────────
        // REPOSITÓRIOS
        // Scoped: mesma instância dentro de uma requisição HTTP,
        // compartilhando o mesmo DbContext e Change Tracker.
        // ─────────────────────────────────────────────────────────────
        services.AddScoped<ITransactionRepository, TransactionRepository>();
        services.AddScoped<IAccountRepository, AccountRepository>();

        // ─────────────────────────────────────────────────────────────
        // UNIT OF WORK
        // Scoped: mesma instância dos repositórios e DbContext
        // dentro da mesma requisição, essencial para consistência
        // transacional (todos compartilham o mesmo Change Tracker).
        // ─────────────────────────────────────────────────────────────
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // Os registros abaixo serão descomentados nos capítulos seguintes:
        // Será implementado
        // - services.AddRabbitMQ(configuration);
        // - services.AddRedisCache(configuration);

        return services;
    }
}