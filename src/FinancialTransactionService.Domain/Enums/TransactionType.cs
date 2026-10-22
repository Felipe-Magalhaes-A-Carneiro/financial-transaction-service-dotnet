namespace FinancialTransactionService.Domain.Enums;

/// <summary>
/// Classifica a natureza financeira de uma transação.
/// Impacta diretamente as regras de validação e contabilização.
/// </summary>
public enum TransactionType
{
    /// <summary>Movimentação de fundos entre duas contas distintas.</summary>
    Transfer = 1,

    /// <summary>Entrada de recursos em uma conta (ex: depósito, recebimento).</summary>
    Credit = 2,

    /// <summary>Saída de recursos de uma conta (ex: saque, pagamento).</summary>
    Debit = 3,

    /// <summary>Pagamento de boleto, tributo ou conta de consumo.</summary>
    Payment = 4,

    /// <summary>Reembolso ou estorno de uma transação anterior.</summary>
    Refund = 5
}