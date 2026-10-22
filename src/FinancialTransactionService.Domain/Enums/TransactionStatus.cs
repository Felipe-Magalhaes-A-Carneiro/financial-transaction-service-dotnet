namespace FinancialTransactionService.Domain.Enums;

/// <summary>
/// Representa o ciclo de vida de uma transação financeira.
/// 
/// Os estados seguem uma máquina de estados unidirecional:
/// Pending -> Processing -> Completed
///                      \> Failed
///          \> Cancelled (apenas de Pending)
/// 
/// </summary>


public enum TransactionStatus
{
    /// <summary> Criada e enfileirada, aguardando processamento assíncrono.</summary>
    Pending = 1,

    /// <summary> Retirada da fila e em processamento pelo consumer.</summary>
    Processing = 2,

    /// <summary Processada com sucesso. Estado terminal positivo.</summary>
    Completed = 3,

    /// <summary> Falhou durante o processamento. Estado terminal negativo.</summary>
    Failed = 4,

    /// <summary> Cancelada antes do processamento. Apenas de Pending.</summary>
    Cancelled = 5
}