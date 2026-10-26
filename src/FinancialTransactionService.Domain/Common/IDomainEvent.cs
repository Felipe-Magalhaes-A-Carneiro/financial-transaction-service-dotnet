namespace FinancialTransactionService.Domain.Common;

/// <summary> 
/// Marcador que identifica um evento de domínio.
/// 
/// Um Domain Event representa um fato que aconteceu no domínio e que outras partes do sistema podem querer conhecer.
/// 
/// - Convenção de nomenclatura será sempre no passado (ex.: TransactionCreated e não TransactionCreate).
/// 
/// - Esta interface é intencionalmente vazia, pois serve como marcador de itpo para que o MediatR consiga descobrir e despachar os handlers corretos via reflexão.
/// 
/// </summary>

public interface IDomainEvent
{
    /// <summary> 
    /// Indentificador único do evento. Garante rastreabilidade em sistemas distribuídos onde o mesmo evento pode ser processado por múltiplos consumers.
    /// </summary>
    Guid EventId {get; }

    /// <summary> 
    /// Timestamp UTC em que o evento ocorreu no domínio.
    /// Diferente de quando foi publicado na fila, o domínio registra o momento exato do fato, e independente de latências de infraestrutura.
    /// </summary>
    DateTime OccurredOn {get; }

}