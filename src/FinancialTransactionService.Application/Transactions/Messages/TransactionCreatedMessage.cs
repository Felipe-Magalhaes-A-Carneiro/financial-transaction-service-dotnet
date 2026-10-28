using FinancialTransactionService.Domain.Enums;

namespace FinancialTransactionService.Application.Transactions.Messages;

/// <summary>
/// Mensagem publicada no broker (RabbitMQ) após a criação bem-sucedida
/// de uma transação financeira.
///
/// Este record define o CONTRATO da mensagem : o schema que o consumer
/// (TransactionConsumerService, séra implementado) usará para deserializar e processar.
/// Mudanças aqui devem ser retrocompatíveis ou versionadas.
///
/// Vive na camada Application (não Infrastructure) porque representa
/// um contrato de comunicação entre serviços — uma responsabilidade
/// de aplicação, não um detalhe de implementação de infraestrutura.
/// </summary>
/// 
/// <param name="MessageId">UUID único desta mensagem para rastreabilidade no broker.</param>
/// <param name="TransactionId">ID da transação a ser processada.</param>
/// <param name="SourceAccountId">Conta de origem (null se não aplicável).</param>
/// <param name="DestinationAccountId">Conta de destino (null se não aplicável).</param>
/// <param name="AmountInCents">Valor em centavos para precisão financeira.</param>
/// <param name="Currency">Código ISO 4217 da moeda.</param>
/// <param name="Type">Tipo da operação financeira.</param>
/// <param name="CorrelationId">ID de correlação para rastreabilidade fim-a-fim.</param>
/// <param name="OccurredOn">Timestamp UTC de quando a transação foi criada.</param>
public sealed record TransactionCreatedMessage(
    Guid MessageId,
    Guid TransactionId,
    Guid? SourceAccountId,
    Guid? DestinationAccountId,
    long AmountInCents,
    string Currency,
    TransactionType Type,
    Guid CorrelationId,
    DateTime OccurredOn
);