using MediatR;

namespace FinancialTransactionService.Application.Transactions.Commands.ProcessTransaction;

/// <summary>
/// Command CQRS para processamento financeiro de uma transação previamente criada.
///
/// Despachado pelo TransactionConsumerService (Background Service, futuramente será adicionado)
/// após consumir uma mensagem da fila "transactions.process.queue" do RabbitMQ.
///
/// Diferença crítica de design em relação ao CreateTransactionCommand:
/// 
/// - CreateTransactionCommand: origem externa (cliente HTTP), validação rigorosa
/// - ProcessTransactionCommand: origem interna (fila RabbitMQ), confiável
///
/// Retorna Unit (void do MediatR): o resultado do processamento é comunicado
/// via mudança de estado da entidade Transaction no banco de dados e pelos
/// Domain Events disparados (TransactionCompletedEvent / TransactionFailedEvent).
/// O consumer não precisa de retorno — o status é observado pela API de consulta.
/// </summary>
/// 
/// <param name="TransactionId">ID da transação a ser processada.</param>
/// <param name="CorrelationId">
/// ID de correlação propagado da mensagem RabbitMQ.
/// Usado nos logs para rastreabilidade fim-a-fim da operação.
/// </param>
public sealed record ProcessTransactionCommand(
    Guid TransactionId,
    Guid CorrelationId

) : IRequest<Unit>;