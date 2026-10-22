using System.Runtime.CompilerServices;
using System.Transactions;
using FinancialTransactionService.Domain.Common;

namespace FinancialTransactionService.Domain.Entities;

/// <summary> 
/// Representa uma conta financeira no sistema.
/// Guarda o estado de saldo e aplica as regras de negócio de débito e crédito de forma encapsulada e segura.
/// </summary> 

public sealed class Account : BaseEntity
{
    // Todos os setters são privados. Nenhum código externo à classe pode alterar o saldo diretamente. A única forma é via os métodos
    // Credit() e Debit() abaixo, que contêm as regras de negócio.    

    /// <summary> Número único e legivel da conta (ex: "0001-9"). </summary>
    public string AccountNumber {get; private set; }

    /// <summary> Nome completo do titular da conta. </summary>   
    public string HolderName {get; private set; }


    /// <summary> 
    /// Saçdp disponível em centavos (BRL), armazenado como long para evitar problemas de precisão de ponto flutuante em cálculos financeiros.
    /// 
    /// </summary>
    public long BalanceInCents {get; private set; }


    /// <summary>Indica se a conta está ativa e pode realizar transações.</summary>
    public bool IsActive {get; private set; }


    /// <summary> Histórico de transações vinculadas a esta conta. </summary>
    /// A coleção é privada e exposta como IReadOnlyCollection para garantir que ningué adicione transações diretamente à lista.
    /// A única forma de associar uma transação a uma conta é via os métodos da conta.
    private readonly List<Transaction> _transactions = [];
    public IReadOnlyCollection<Transaction> Transactions => _transactions.AsReadOnly();


    // Construtor privado sem parâmetros EXCLUSIVO para o EF Core.
    // Sem ele, o EF Core não consegue instanciar objetos do banco.
    // O '#pragma warning disable' suprime o aviso do compilador sobre campo não inicializado
#pragma warning disable CS8618
    private Account() { }
#pragma warning restore CS8618

    /// <summary>
    /// Cria uma nova conta financeira validando as invariantes de domínio
    /// </summary>
    /// <param name = "accountNumber"> Número único da conta. Não pode ser vazio. </param>
    /// <param name = "holderName"> Nome do titular. Não pode ser vazio. </param>
    /// <param name = "initialBalanceInCents"> Saldo inicial em centavos. Não pode ser negativo.</param> 
    /// <exception cref="ArgumenException"> Lançada quando algum argumento é inválido. </exception>
    public Account(string accountNumber, string holderName, long initialBalanceInCents = 0)
    {
        // Estas validações são "guardas de invariante" (invariant guards).
        // Elas garantem que um objeto Account NUNCA existirá em estado inválido.
        // Em sistemas financeiros, uma conta com nome vazio ou saldo negativo inicial é um bug de dados que pode ter consequências legais.
        if (string.IsNullOrWhiteSpace(accountNumber))
            throw new ArgumentException("Número de conta não pode ser vazio.", nameof(accountNumber));

        if (string.IsNullOrWhiteSpace(holderName))
            throw new ArgumentException("Nome do titular não pode ser vazio.", nameof(holderName));

        if (initialBalanceInCents < 0)
            throw new ArgumentException("Saldo inicial não pode ser negativo.", nameof(initialBalanceInCents));

        AccountNumber = accountNumber;
        HolderName = holderName;
        BalanceInCents = initialBalanceInCents;
        IsActive = true;        
    }

    /// <summary>
    /// Credita um valor em centavos ao saldo da conta.
    /// </summary>
    /// <param name="amountInCents">Valor a creditar. Deve ser positivo.</param>
    /// <exception cref="ArgumentException">Lançada se o valor for zero ou negativo.</exception>
    /// <exception cref="InvalidOperationException">Lançada se a conta estiver inativa.</exception>
    public void Credit(long amountInCents)
    {
        EnsureAccountIsActive();

        if (amountInCents <= 0)
            throw new ArgumentException(
                "Valor de crédito deve ser positivo.",
                nameof(amountInCents));

        BalanceInCents += amountInCents;
        SetUpdatedAt();
    }

    /// <summary>
    /// Debita um valor em centavos do saldo da conta.
    /// Verifica saldo suficiente antes de executar.
    /// </summary>
    /// <param name="amountInCents">Valor a debitar. Deve ser positivo.</param>
    /// <exception cref="ArgumentException">Lançada se o valor for zero ou negativo.</exception>
    /// <exception cref="InvalidOperationException">
    /// Lançada se a conta estiver inativa ou com saldo insuficiente.
    /// </exception>
    public void Debit(long amountInCents)
    {
        EnsureAccountIsActive();

        if (amountInCents <= 0)
            throw new ArgumentException(
                "Valor de débito deve ser positivo.",
                nameof(amountInCents));

        if (BalanceInCents < amountInCents)
            throw new InvalidOperationException(
                $"Saldo insuficiente. Disponível: {BalanceInCents} centavos. " +
                $"Solicitado: {amountInCents} centavos.");

        BalanceInCents -= amountInCents;
        SetUpdatedAt();
    }

    /// <summary>
    /// Desativa a conta, impedindo novas transações.
    /// </summary>
    /// <exception cref="InvalidOperationException"> Lançada se a conta já estiver inativa.</exception>
    public void Deactivate()
    {
        if (!IsActive)
            throw new InvalidOperationException(
                $"Conta '{AccountNumber}' já está inativa.");

        IsActive = false;
        SetUpdatedAt();
    }

    /// <summary>
    /// Retorna o saldo em reais (formato decimal) para exibição.
    /// Internamente, o saldo é armazenado em centavos para evitar erros de precisão.
    /// </summary>
    public decimal GetBalanceInReais() => BalanceInCents / 100m;

    // Guard method — extrai a validação de conta ativa para um método privado reutilizado por Credit() e Debit(). Princípio DRY aplicado ao domínio.
    private void EnsureAccountIsActive()
    {
        if (!IsActive)
            throw new InvalidOperationException(
                $"Conta '{AccountNumber}' está inativa e não pode realizar transações.");
    }
    


}