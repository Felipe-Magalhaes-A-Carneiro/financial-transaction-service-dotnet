using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FinancialTransactionService.Application.Common.Behaviors;

/// <summary>
/// Pipeline Behavior que provê logging estruturado e medição de performance
/// para todos os Commands e Queries despachados via MediatR.
///
/// Executa para TODOS os IRequest - Commands e Queries - sem necessidade
/// de modificar nenhum handler individualmente.
///
/// Emite dois eventos de log por execução:
///   1. "Handling {RequestName}" - antes da execução (com dados do request)
///   2. "Handled {RequestName} in {Ms}ms" — após a execução (com duração)
///
/// Emite WARNING quando a execução excede o threshold de performance,
/// permitindo detectar queries lentas e operações degradadas proativamente.
/// 
/// </summary>
/// 
/// <typeparam name="TRequest">Tipo do Command ou Query.</typeparam>
/// <typeparam name="TResponse">Tipo do resultado esperado.</typeparam>
public sealed class LoggingBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;

    // Threshold de performance: operações acima deste tempo emitem WARNING.
    // Em sistemas financeiros, operações síncronas devem responder em < 500ms.
    // Operações acima de 1s são candidatas à investigação imediata.
    private const int WarningThresholdMs = 500;

    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;

        // Usamos LogDebug para o log de início porque em produção
        // sob alta carga, logar o request completo em Information geraria
        // volume excessivo. Debug permite ativar apenas quando necessário
        // sem impacto em produção.
        _logger.LogDebug(
            "Iniciando handler. Request: {RequestName}. Dados: {@Request}.",
            requestName,
            request);

        var stopwatch = Stopwatch.StartNew();

        TResponse response;

        try
        {
            // Invoca o próximo behavior na pipeline ou o handler final
            response = await next();
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            // Logamos o erro aqui no behavior, mas NÃO o suprimimos.
            // O re-throw garante que o ExceptionHandlingMiddleware da API (futuramente implementado)
            // receba a exceção e decida o código HTTP correto.
            // Logar e re-throw é diferente de swallow ("engolir") a exceção.
            _logger.LogError(
                ex,
                "Handler falhou. Request: {RequestName}. " +
                "Duração até a falha: {ElapsedMs}ms. " +
                "Erro: {ErrorMessage}.",
                requestName,
                stopwatch.ElapsedMilliseconds,
                ex.Message);

            throw;
        }

        stopwatch.Stop();
        var elapsedMs = stopwatch.ElapsedMilliseconds;

        // ──────────────────────────────────────────────────────
        // DETECÇÃO DE PERFORMANCE DEGRADADA
        // Emite WARNING para operações lentas sem precisar de
        // ferramentas externas de profiling em desenvolvimento.
        // ──────────────────────────────────────────────────────
        if (elapsedMs >= WarningThresholdMs)
        {
            _logger.LogWarning(
                "Handler lento detectado. Request: {RequestName}. " +
                "Duração: {ElapsedMs}ms. " +
                "Threshold: {ThresholdMs}ms. " +
                "Investigue possíveis N+1 queries, locks de banco ou " +
                "chamadas externas sem timeout adequado.",
                requestName,
                elapsedMs,
                WarningThresholdMs);
        }
        else
        {
            _logger.LogInformation(
                "Handler concluído. Request: {RequestName}. " +
                "Duração: {ElapsedMs}ms.",
                requestName,
                elapsedMs);
        }

        return response;
    }
}