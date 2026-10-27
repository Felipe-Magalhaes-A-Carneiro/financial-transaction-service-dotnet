using FluentValidation.Results;

namespace FinancialTransactionService.Application.Common.Exceptions;

/// <summary>
/// Exceção lançada pelo ValidationBehavior quando um Command ou Query falha na validação do FluentValidation antes de chegar ao Handler.
///
/// Diferença de design em relação às DomainExceptions:
/// - DomainException: regra de negócio violada (ex: saldo insuficiente)
///   -> o dado chegou ao Handler e foi rejeitado pelas invariantes do domínio
/// 
/// - ValidationException: input malformado (ex: valor negativo, campo vazio)
///   -> o dado foi rejeitado ANTES de chegar ao Handler
///   -> HTTP 400 Bad Request (input inválido, não regra de negócio)
///
/// Carrega todos os erros de validação agrupados por campo para que o ExceptionHandlingMiddleware (será implementado no futuro) possa retornar uma resposta estruturada e acionável ao cliente da API.
/// </summary>
public sealed class ValidationException : Exception
{
    /// <summary>
    /// Dicionário de erros de validação agrupados por nome de campo.
    /// Chave: nome do campo (ex: "Amount", "SourceAccountId").
    /// Valor: array de mensagens de erro para aquele campo.
    ///
    /// Ex: { "Amount": ["Valor deve ser positivo.", "Valor máximo: R$ 50.000,00."] }
    /// </summary>
    public IDictionary<string, string[]> Errors { get; }

    /// <summary>
    /// Cria uma ValidationException a partir dos resultados do FluentValidation.
    /// Agrupa os erros por nome de propriedade automaticamente.
    /// </summary>
    /// 
    /// <param name="failures">Lista de falhas retornada pelo FluentValidation.</param>
    public ValidationException(IEnumerable<ValidationFailure> failures)
        : base("Um ou mais erros de validação ocorreram.")
    {
        // GroupBy agrupa as falhas pelo nome do campo (PropertyName).
        // Para cada campo, coletamos todas as mensagens de erro.
        // Isso gera o formato padrão de validação da RFC 7807 (Problem Details).
        Errors = failures
            .GroupBy(
                keySelector: failure => failure.PropertyName,
                elementSelector: failure => failure.ErrorMessage)
            .ToDictionary(
                keySelector: group => group.Key,
                elementSelector: group => group.ToArray());
    }
}