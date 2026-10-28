using FinancialTransactionService.Domain.Enums;
using FluentValidation;

namespace FinancialTransactionService.Application.Transactions.Commands.CreateTransaction;

/// <summary>
/// Validator do FluentValidation para CreateTransactionCommand.
///
/// Responsabilidade: validar o FORMAT e RANGE dos dados de entrada
/// ANTES de chegarem ao handler, sem tocar em banco de dados.
/// 
/// Regras de negócio profundas (saldo suficiente, conta existente)
/// ficam no Handler, pois requerem consulta ao repositório.
///
/// O ValidationBehavior (será implementado) executa este validator automaticamente
/// para todo CreateTransactionCommand recebido pelo MediatR.
/// </summary>
public sealed class CreateTransactionCommandValidator
    : AbstractValidator<CreateTransactionCommand>
{
    // Limite máximo por transação: R$ 50.000,00
    // Em produção, este valor viria de configuração por tipo de conta/cliente.
    private const decimal MaxTransactionAmountBrl = 50_000m;

    public CreateTransactionCommandValidator()
    {
        // ──────────────
        // CORRELATIONID
        // ──────────────
        RuleFor(c => c.CorrelationId)
            .NotEmpty()
            .WithMessage("X-Correlation-Id é obrigatório e deve ser um UUID v4 válido.");

        // ──────────────
        // VALOR E MOEDA
        // ──────────────
        RuleFor(c => c.Amount)
            .GreaterThan(0)
                .WithMessage("Valor da transação deve ser maior que zero.")
            .LessThanOrEqualTo(MaxTransactionAmountBrl)
                .WithMessage($"Valor máximo por transação: R$ {MaxTransactionAmountBrl:N2}.");

        RuleFor(c => c.Currency)
            .NotEmpty()
                .WithMessage("Código de moeda é obrigatório.")
            .Length(3)
                .WithMessage("Código de moeda deve ter exatamente 3 caracteres (ISO 4217).")
            .Matches("^[A-Za-z]{3}$")
                .WithMessage("Código de moeda deve conter apenas letras (ex: BRL, USD, EUR).");

        // ──────────────────
        // TIPO DA TRANSAÇÃO
        // ──────────────────
        RuleFor(c => c.Type)
            .IsInEnum()
            .WithMessage("Tipo de transação inválido.");

        // ─────────────────────────────────────────
        // CONTAS: VALIDAÇÕES CONDICIONAIS POR TIPO
        // ─────────────────────────────────────────

        // Transfer: ambas as contas são obrigatórias e devem ser distintas
        When(c => c.Type == TransactionType.Transfer, () =>
        {
            RuleFor(c => c.SourceAccountId)
                .NotNull()
                .WithMessage("Transferências requerem conta de origem (SourceAccountId).");

            RuleFor(c => c.DestinationAccountId)
                .NotNull()
                .WithMessage("Transferências requerem conta de destino (DestinationAccountId).");

            RuleFor(c => c)
                .Must(c => c.SourceAccountId != c.DestinationAccountId)
                .WithName("Accounts")
                .WithMessage("Conta de origem e destino não podem ser iguais em uma transferência.");
        });

        // Debit e Payment: apenas conta de origem
        When(c => c.Type == TransactionType.Debit || c.Type == TransactionType.Payment, () =>
        {
            RuleFor(c => c.SourceAccountId)
                .NotNull()
                .WithMessage($"Operações do tipo {{PropertyValue}} requerem conta de origem.")
                .OverridePropertyName("SourceAccountId");
        });

        // Credit e Refund: apenas conta de destino
        When(c => c.Type == TransactionType.Credit || c.Type == TransactionType.Refund, () =>
        {
            RuleFor(c => c.DestinationAccountId)
                .NotNull()
                .WithMessage($"Operações do tipo {{PropertyValue}} requerem conta de destino.")
                .OverridePropertyName("DestinationAccountId");
        });

        // ─────────
        // DESCRIÇÃO
        // ─────────
        RuleFor(c => c.Description)
            .MaximumLength(500)
            .WithMessage("Descrição não pode exceder 500 caracteres.")
            .When(c => c.Description is not null);
    }
}