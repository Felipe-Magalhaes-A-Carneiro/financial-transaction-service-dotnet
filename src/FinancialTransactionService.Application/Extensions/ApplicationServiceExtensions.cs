using System.Reflection;
using FinancialTransactionService.Application.Common.Behaviors;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialTransactionService.Application.Extensions;

/// <summary>
/// Extensões para registro dos serviços da camada Application no container de DI.
/// Versão final com Pipeline Behaviors registrados e ordenados corretamente.
/// 
/// </summary>
public static class ApplicationServiceExtensions
{
    /// <summary>
    /// Registra todos os serviços da camada Application:
    /// MediatR com Pipeline Behaviors ordenados, e Validators do FluentValidation.
    /// 
    /// </summary>
    public static IServiceCollection AddApplicationServices(
        this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddMediatR(config =>
        {
            config.RegisterServicesFromAssembly(assembly);

            // ─────────────────────────────────────────────────────────
            // ORDEM DOS BEHAVIORS - A ORDEM IMPORTA.
            //
            // A pipeline é construída de fora para dentro (como middleware):
            // o primeiro registrado é o mais externo (executa primeiro antes,
            // último depois).
            //
            // Ordem correta:
            //   LoggingBehavior -> ValidationBehavior → Handler
            //
            // Por quê esta ordem?
            //   1. LoggingBehavior: mede o tempo TOTAL incluindo validação.
            //      Se registrássemos após Validation, não mediríamos o tempo
            //      gasto na validação em si.
            //
            //   2. ValidationBehavior: rejeita inputs inválidos ANTES de chegar ao handler. Nenhuma lógica de negócio é executada com dados ruins.
            //
            //   3. Handler: executa apenas se a validação passou.
            // ─────────────────────────────────────────────────────────
            config.AddOpenBehavior(typeof(LoggingBehavior<,>));
            config.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });

        services.AddValidatorsFromAssembly(
            assembly: assembly,
            lifetime: ServiceLifetime.Scoped,
            includeInternalTypes: false);

        return services;
    }
}