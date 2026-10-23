namespace FinancialTransactionService.Domain.ValueObjects;

/// <summary>
/// Value Object que representa o identificador único de uma transação.
///
/// Justificativa de existência:
/// Usar Guid puro como identificador de transação é um exemplo de Primitive Obsession.
/// Com um tipo dedicado, o compilador impede que você passe acidentalmente
/// um AccountId onde um TransactionId é esperado ambos seriam Guid sem este tipo.
///
/// Ex sem este tipo: 
///     o compilador NÃO detecta a troca acidental:
///     void Process(Guid transactionId, Guid accountId) { ... }
///     Process(accountId, transactionId); // compila, bug em produção
///
/// Ex com este tipo: 
///     erro de compilação imediato:
///     void Process(TransactionId id, AccountId accountId) { ... }
///     Process(accountId, transactionId); // não compila
/// </summary>
public sealed record TransactionId
{
    /// <summary>O valor bruto do identificador.</summary>
    public Guid Value { get; }

    /// <summary>
    /// Cria um TransactionId a partir de um Guid existente.
    /// </summary>
    /// 
    /// <param name="value"> Não pode ser Guid.Empty.</param>
    /// <exception cref="ArgumentException">Lançada se o Guid for vazio.
    /// </exception>
    public TransactionId(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException(
                "TransactionId não pode ser um Guid vazio.",
                nameof(value));

        Value = value;
    }

    /// <summary>
    /// Gera um novo TransactionId único.
    /// Factory method semântico — deixa clara a intenção de criar um novo ID.
    /// </summary>
    public static TransactionId New() => new(Guid.NewGuid());

    /// <summary>
    /// Reconstrói um TransactionId a partir de uma string (ex: vindo de um header HTTP).
    /// </summary>
    /// 
    /// <param name="value"> String no formato UUID v4.</param>
    /// <exception cref="FormatException"> Lançada se a string não for um Guid válido.
    /// </exception>
    public static TransactionId Parse(string value)
    {
        if (!Guid.TryParse(value, out var guid))
            throw new FormatException(
                $"'{value}' não é um TransactionId válido. Formato esperado: UUID v4.");

        return new TransactionId(guid);
    }

    /// <summary>
    /// Tenta reconstrói um TransactionId sem lançar exceção em caso de falha.
    /// Útil para parsing de input de usuário ou headers HTTP.
    /// </summary>
    public static bool TryParse(string value, out TransactionId? transactionId)
    {
        transactionId = null;

        if (!Guid.TryParse(value, out var guid) || guid == Guid.Empty)
            return false;

        transactionId = new TransactionId(guid);
        return true;
    }

    // ─────────────────────────────────────────────────────────────
    // CONVERSÕES IMPLÍCITAS E EXPLÍCITAS
    // Permitem interoperabilidade com APIs e EF Core sem fricção
    // ─────────────────────────────────────────────────────────────

    // A conversão implícita de TransactionId → Guid permite usar o Value Object
    // diretamente onde um Guid é esperado (ex: parâmetros de EF Core, LINQ queries).
    // A conversão é IMPLÍCITA nesta direção porque é sempre segura — nunca perde informação.
    public static implicit operator Guid(TransactionId id) => id.Value;

    // A conversão de Guid → TransactionId é EXPLÍCITA (requer cast)
    // porque pode falhar (Guid.Empty). O programador precisa declarar a intenção.
    public static explicit operator TransactionId(Guid guid) => new(guid);

    public override string ToString() => Value.ToString();
}