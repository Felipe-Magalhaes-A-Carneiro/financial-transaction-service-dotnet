using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialTransactionService.Application.Extensions;

/// <summary>
/// Extensões para registro dos serviços da camada Application no container de DI.
///
/// Design: toda camada expõe um único método de extensão que encapsula
/// todos os seus registros internos. O Program.cs (implementado no futuro) chama apenas:
///   builder.Services.AddApplicationServices(); Sem precisar conhecer os detalhes internos da camada.
///
/// Este padrão é chamado de "Composition Root Encapsulation" e é
/// amplamente adotado em times de engenharia de grande porte.
/// </summary>
public static class ApplicationServiceExtensions
{
    /// <summary>
    /// Registra todos os serviços da camada Application:
    /// MediatR (Commands, Queries, Handlers), Pipeline Behaviors e Validators do FluentValidation.
    /// </summary>
    /// 
    /// <param name="services">O container de serviços do .NET.</param>
    /// 
    /// <returns>O container para encadeamento fluente.</returns>
    public static IServiceCollection AddApplicationServices(
        this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        // ─────────────────────────────────────────────────────────────
        // MEDIATR
        // Registra automaticamente todos os IRequestHandler<,> e
        // INotificationHandler<> encontrados neste assembly.
        // ─────────────────────────────────────────────────────────────
        services.AddMediatR(config =>
        {
            config.RegisterServicesFromAssembly(assembly);

            // Os Pipeline Behaviors são registrados aqui em ordem.
            // A ordem importa: cada behavior envolve o próximo como camadas de cebola.
            // LoggingBehavior -> ValidationBehavior -> Handler
            // Implementados no futuro, as linhas abaixo serão descomentadas lá:
            //
            // config.AddOpenBehavior(typeof(LoggingBehavior<,>));
            // config.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });

        // ─────────────────────────────────────────────────────────────
        // FLUENTVALIDATION
        // Registra automaticamente todos os AbstractValidator<T>
        // encontrados neste assembly como IValidator<T> no container.
        // ─────────────────────────────────────────────────────────────
        services.AddValidatorsFromAssembly(
            assembly: assembly,
            // ServiceLifetime.Scoped é o padrão correto para validators
            // que podem ter dependências de serviços com escopo por requisição.
            lifetime: ServiceLifetime.Scoped,
            // Não registra validators de tipos abertos (genéricos) automaticamente
            includeInternalTypes: false);

        return services;
    }
}