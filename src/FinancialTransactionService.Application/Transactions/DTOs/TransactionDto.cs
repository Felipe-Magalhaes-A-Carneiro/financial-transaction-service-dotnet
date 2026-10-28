using FinancialTransactionService.Domain.Entities;

namespace FinancialTransactionService.Application.Transactions.DTOs;

/// <summary>
/// Data Transfer Object que representa uma transação financeira na resposta da API.
///
/// Design: mapeamento de entidade -> DTO feito via factory method estático.
/// Isso centraliza a lógica de projeção e evita que controllers ou handlers
/// precisem conhecer os detalhes internos da entidade.
///
/// Usamos string para Type e Status (em vez de enum) para desacoplar
/// o contrato da API dos valores internos do enum — mudanças no enum
/// não quebram clientes da API que consumam o JSON.
/// </summary>
public sealed record TransactionDto
{
    /// <summary> 
    /// Identificador único interno da transação.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// ID de correlação externo usado para idempotência e rastreabilidade.
    /// </summary>
    public Guid CorrelationId { get; init; }

    public Guid? SourceAccountId { get; init; }
    public Guid? DestinationAccountId { get; init; }

    /// <summary>
    /// Valor em centavos — para integração com outros sistemas internos.
    /// </summary>
    public long AmountInCents { get; init; }

    /// <summary>
    /// Valor em reais — para exibição ao usuário final.
    /// </summary>
    public decimal AmountInReais { get; init; }

    /// <summary>Código ISO 4217 da moeda (ex: "BRL").</summary>
    public string Currency { get; init; } = string.Empty;

    /// <summary>Tipo da operação em formato legível (ex: "Transfer", "Debit").</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>
    /// Estado atual em formato legível (ex: "Pending", "Completed").
    /// </summary>
    public string Status { get; init; } = string.Empty;

    public string? Description { get; init; }

    /// <summary>
    /// Motivo de falha ou cancelamento. Null se a transação está ativa.
    /// </summary>
    public string? FailureReason { get; init; }

    /// <summary>
    /// Timestamp UTC de criação.
    /// </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// Timestamp UTC em que o processamento assíncrono foi iniciado. Null se ainda Pending.
    /// </summary>
    public DateTime? ProcessedAt { get; init; }

    // Factory method estático — a única forma de criar um TransactionDto
    // a partir de uma entidade de domínio. Centraliza a lógica de mapeamento
    // e facilita ajustes futuros sem impactar callers.

    /// <summary>
    /// Cria um TransactionDto a partir de uma entidade Transaction do domínio.
    /// </summary>
    /// 
    /// <param name="transaction">Entidade de domínio totalmente carregada.</param>
    /// 
    /// <returns>DTO pronto para serialização e retorno ao cliente.</returns>
    public static TransactionDto FromTransaction(Transaction transaction) => new()
    {
        Id                   = transaction.Id,
        CorrelationId        = transaction.CorrelationId,
        SourceAccountId      = transaction.SourceAccountId,
        DestinationAccountId = transaction.DestinationAccountId,
        AmountInCents        = transaction.AmountInCents,
        AmountInReais        = transaction.GetAmountInReais(),
        Currency             = transaction.Currency,
        Type                 = transaction.Type.ToString(),
        Status               = transaction.Status.ToString(),
        Description          = transaction.Description,
        FailureReason        = transaction.FailureReason,
        CreatedAt            = transaction.CreatedAt,
        ProcessedAt          = transaction.ProcessedAt
    };
}