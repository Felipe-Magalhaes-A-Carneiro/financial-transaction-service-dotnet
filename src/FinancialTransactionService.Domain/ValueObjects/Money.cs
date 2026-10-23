namespace FinancialTransactionService.Domain.ValueObjects;

/// <summary>
/// Value Object que representa um valor monetário com sua moeda.
/// Encapsula todas as regras de validação e operações aritméticas financeiras, eliminando o anti-pattern de Primitive Obsession com decimal/string soltos.
///
/// Imutável por design: toda operação aritmética retorna um NOVO Money,
/// nunca modifica o original — comportamento idêntico ao de um número.
/// 
/// </summary>

/// <param name="Amount">Valor monetário. Deve ser estritamente positivo.</param>
/// <param name="Currency">Código ISO 4217 da moeda (ex: "BRL", "USD", "EUR").</param>
public sealed record Money
{
    // Listamos as moedas suportadas como um HashSet para lookup O(1).
    // Em um sistema real, isso viria de um serviço de configuração ou banco de dados. Para o portfólio, a lista fixa demonstra o conceito com clareza.
    private static readonly HashSet<string> SupportedCurrencies =
        ["BRL", "USD", "EUR", "GBP", "JPY", "CHF", "ARS", "CLP", "COP", "MXN"];

    /// <summary>
    /// Valor monetário com precisão de até 4 casas decimais.
    /// </summary>
    public decimal Amount { get; }

    /// <summary>
    /// Código ISO 4217 da moeda (sempre em maiúsculas).
    /// </summary>
    public string Currency { get; }

    /// <summary>
    /// Cria uma instância de Money validando valor e moeda.
    /// </summary>
    /// 
    /// <param name="amount">Deve ser maior que zero.</param>
    /// <param name="currency">Deve ser um código ISO 4217 suportado.</param>
    /// <exception cref="ArgumentException">
    /// Lançada quando amount é zero/negativo ou currency é inválida/não suportada.
    /// </exception>
    public Money(decimal amount, string currency)
    {
        if (amount <= 0)
            throw new ArgumentException(
                $"Valor monetário deve ser positivo. Recebido: {amount}.",
                nameof(amount));

        if (string.IsNullOrWhiteSpace(currency))
            throw new ArgumentException(
                "Código de moeda não pode ser vazio.",
                nameof(currency));

        // Normalizamos para maiúsculas para aceitar "brl", "Brl" e "BRL" igualmente.
        // Defensivo: nunca confie que o caller formatou corretamente.
        var normalizedCurrency = currency.Trim().ToUpperInvariant();

        if (!SupportedCurrencies.Contains(normalizedCurrency))
            throw new ArgumentException(
                $"Moeda '{normalizedCurrency}' não é suportada. " +
                $"Moedas aceitas: {string.Join(", ", SupportedCurrencies)}.",
                nameof(currency));

        Amount = Math.Round(amount, 4, MidpointRounding.AwayFromZero);
        Currency = normalizedCurrency;
    }

    // ─────────────────────────────────────────────────────────────
    // OPERAÇÕES ARITMÉTICAS
    // Cada operação retorna um NOVO Money — imutabilidade garantida.
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Soma dois valores monetários da mesma moeda.
    /// </summary>
    /// 
    /// <exception cref="InvalidOperationException">
    /// Lançada ao tentar somar moedas diferentes (ex: BRL + USD).
    /// </exception>
    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount + other.Amount, Currency);
    }

    /// <summary>
    /// Subtrai um valor monetário. O resultado deve ser positivo.
    /// </summary>
    /// 
    /// <exception cref="InvalidOperationException">
    /// Lançada ao tentar subtrair moedas diferentes ou se o resultado for negativo.
    /// </exception>
    public Money Subtract(Money other)
    {
        EnsureSameCurrency(other);

        var result = Amount - other.Amount;

        if (result <= 0)
            throw new InvalidOperationException(
                $"Subtração resultaria em valor não positivo ({result}). " +
                "Verifique se há saldo suficiente antes de subtrair.");

        return new Money(result, Currency);
    }

    /// <summary>
    /// Multiplica o valor por um fator escalar (ex: taxas, juros, impostos).
    /// </summary>
    /// 
    /// <param name="factor">Deve ser positivo.</param>
    /// <exception cref="ArgumentException">Lançada se o fator for zero ou negativo.
    /// </exception>
    public Money MultiplyBy(decimal factor)
    {
        if (factor <= 0)
            throw new ArgumentException(
                $"Fator de multiplicação deve ser positivo. Recebido: {factor}.",
                nameof(factor));

        return new Money(Amount * factor, Currency);
    }

    // ─────────────────────────────────────────────────────────────
    // COMPARAÇÕES
    // ─────────────────────────────────────────────────────────────

    /// <summary>Verifica se este valor é maior que outro da mesma moeda.</summary>
    public bool IsGreaterThan(Money other)
    {
        EnsureSameCurrency(other);
        return Amount > other.Amount;
    }

    /// <summary>Verifica se este valor é menor que outro da mesma moeda.</summary>
    public bool IsLessThan(Money other)
    {
        EnsureSameCurrency(other);
        return Amount < other.Amount;
    }

    /// <summary>Verifica se este valor é maior ou igual a outro da mesma moeda.</summary>
    public bool IsGreaterThanOrEqual(Money other)
    {
        EnsureSameCurrency(other);
        return Amount >= other.Amount;
    }

    // ─────────────────────────────────────────────────────────────
    // CONVERSÃO E UTILITÁRIOS
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Converte o valor para centavos (long) para armazenamento no banco de dados.
    /// Evita problemas de precisão de ponto flutuante em operações de persistência.
    /// </summary>
    public long ToAmountInCents() =>
        (long)Math.Round(Amount * 100, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Cria um Money a partir de um valor em centavos.
    /// Factory method para reconstrução a partir do banco de dados.
    /// </summary>
    /// <param name="amountInCents">Valor em centavos (ex: 15075 = R$ 150,75).</param>
    /// <param name="currency">Código ISO 4217 da moeda.</param>
    public static Money FromCents(long amountInCents, string currency) =>
        new(amountInCents / 100m, currency);

    /// <summary>
    /// Retorna uma representação legível formatada com símbolo de moeda.
    /// Ex: Money(150.75, "BRL") → "R$ 150,75"
    /// </summary>
    public string ToFormattedString()
    {
        var symbol = Currency switch
        {
            "BRL" => "R$",
            "USD" => "US$",
            "EUR" => "€",
            "GBP" => "£",
            "JPY" => "¥",
            _     => Currency
        };

        return $"{symbol} {Amount:N2}";
    }

    // O record já gera ToString() automaticamente no formato "Money { Amount = 150, Currency = BRL }".
    // Sobrescrevemos aqui para um formato mais limpo e útil em logs financeiros.
    public override string ToString() => $"{Amount:F4} {Currency}";

    // ─────────────────────────────────────────────────────────────
    // OPERADORES SOBRECARREGADOS
    // Permitem sintaxe natural: money1 + money2, money1 > money2
    // ─────────────────────────────────────────────────────────────

    public static Money operator +(Money left, Money right) => left.Add(right);
    public static Money operator -(Money left, Money right) => left.Subtract(right);
    public static Money operator *(Money money, decimal factor) => money.MultiplyBy(factor);

    public static bool operator >(Money left, Money right)  => left.IsGreaterThan(right);
    public static bool operator <(Money left, Money right)  => left.IsLessThan(right);
    public static bool operator >=(Money left, Money right) => left.IsGreaterThanOrEqual(right);
    public static bool operator <=(Money left, Money right) => right.IsGreaterThanOrEqual(left);

    // ─────────────────────────────────────────────────────────────
    // GUARD PRIVADO
    // ─────────────────────────────────────────────────────────────

    private void EnsureSameCurrency(Money other)
    {
        if (Currency != other.Currency)
            throw new InvalidOperationException(
                $"Não é possível operar moedas diferentes: '{Currency}' e '{other.Currency}'. " +
                "Uma conversão cambial explícita é necessária.");
    }
}