using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using ValidationException =
    FinancialTransactionService.Application.Common.Exceptions.ValidationException;

namespace FinancialTransactionService.Application.Common.Behaviors;

/// <summary>
/// Pipeline Behavior que executa validação com FluentValidation para todos
/// os Commands e Queries que possuem um Validator registrado no container de DI.
///
/// Executa ANTES do handler - garante que dados inválidos nunca alcançam
/// a lógica de negócio, separando claramente:
///   - ValidationBehavior: formato e range do input (HTTP 400)
///   - Handler + DomainExceptions: regras de negócio (HTTP 422)
///
/// Comportamento quando não há Validator:
///   Se nenhum IValidator&lt;TRequest&gt; for registrado para o tipo do request
///   (ex: GetTransactionByIdQuery não tem validator), a lista _validators
///   vem vazia e o behavior passa direto para o próximo na pipeline.
///   Zero overhead para requests sem validação.
/// </summary>
/// 
/// <typeparam name="TRequest">Tipo do Command ou Query.</typeparam>
/// <typeparam name="TResponse">Tipo do resultado esperado.</typeparam>
public sealed class ValidationBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;
    private readonly ILogger<ValidationBehavior<TRequest, TResponse>> _logger;

    // O DI injeta TODOS os IValidator<TRequest> registrados para este tipo.
    // Para CreateTransactionCommand -> injeta [CreateTransactionCommandValidator].
    // Para GetTransactionByIdQuery  -> injeta [] (lista vazia, nenhum validator).
    // Isso é possível porque registramos com AddValidatorsFromAssembly() anteriormente
    public ValidationBehavior(
        IEnumerable<IValidator<TRequest>> validators,
        ILogger<ValidationBehavior<TRequest, TResponse>> logger)
    {
        _validators = validators;
        _logger     = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // Atalho: se não há validators para este tipo, vai direto ao próximo
        if (!_validators.Any())
            return await next();

        var requestName = typeof(TRequest).Name;

        _logger.LogDebug(
            "Executando validação. Request: {RequestName}. " +
            "Validators encontrados: {ValidatorCount}.",
            requestName,
            _validators.Count());

        // ─────────────────────────────────────────────────────────────
        // EXECUÇÃO PARALELA DOS VALIDATORS
        //
        // Se houver múltiplos validators para o mesmo tipo (incomum, mas
        // possível), os executamos todos em paralelo com Task.WhenAll.
        // Coletamos TODOS os erros antes de lançar a exceção - evitamos
        // o problema de "one error at a time" que força o usuário a
        // submeter o formulário várias vezes para descobrir todos os erros.
        // ─────────────────────────────────────────────────────────────
        var context = new ValidationContext<TRequest>(request);

        var validationResults = await Task.WhenAll(
            _validators.Select(v => v.ValidateAsync(context, cancellationToken)));

        var failures = validationResults
            .Where(result => !result.IsValid)
            .SelectMany(result => result.Errors)
            .ToList();

        if (failures.Count == 0)
        {
            _logger.LogDebug(
                "Validação aprovada. Request: {RequestName}.",
                requestName);

            return await next();
        }

        // ─────────────────────────────────────────────────────────────
        // FALHA DE VALIDAÇÃO
        // Logamos os erros de forma estruturada para observabilidade e
        // lançamos nossa ValidationException com todos os erros agrupados
        // por campo - o ExceptionHandlingMiddleware (futuramente implementado) converterá
        // para HTTP 400 com body no formato Problem Details (RFC 7807).
        // ─────────────────────────────────────────────────────────────
        _logger.LogWarning(
            "Validação falhou. Request: {RequestName}. " +
            "Total de erros: {ErrorCount}. " +
            "Erros: {@ValidationErrors}.",
            requestName,
            failures.Count,
            failures.Select(f => new
            {
                Field   = f.PropertyName,
                Message = f.ErrorMessage
            }));

        throw new ValidationException(failures);
    }
}