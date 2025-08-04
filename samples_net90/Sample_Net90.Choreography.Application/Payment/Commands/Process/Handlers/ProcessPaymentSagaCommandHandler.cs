using Lycia.Saga.Handlers;
using MapsterMapper;
using Microsoft.Extensions.Logging;
using Sample_Net90.Choreography.Application.Interfaces.Repositories;
using Sample_Net90.Choreography.Application.Interfaces.Services;
using Sample_Net90.Choreography.Domain.Sagas.Payment.ProcessPayment.Commands;
using Sample_Net90.Choreography.Domain.Sagas.Payment.ProcessPayment.Events;
using Sample_Net90.Choreography.Domain.Sagas.Stock.ReserveStock.Events;

namespace Sample_Net90.Choreography.Application.Payment.Commands.Process.Handlers;

public sealed class ProcessPaymentSagaCommandHandler(ILogger<ProcessPaymentSagaCommandHandler> logger, IMapper mapper, IPaymentService paymentService, IOrderRepository orderRepository)
    : StartReactiveSagaHandler<ProcessPaymentSagaCommand>
{
    public override async Task HandleStartAsync(ProcessPaymentSagaCommand message)
    {
        try
        {
            if(!await orderRepository.OrderExistsAsync(message.OrderId))
            {
                logger.LogWarning("ProcessPaymentSagaCommandHandler => HandleStartAsync => Order with ID: {OrderId} does not exist. Cannot process payment.", message.OrderId);
                throw new InvalidOperationException($"Order with ID: {message.OrderId} does not exist.");
            }

            logger.LogInformation("ProcessPaymentSagaCommandHandler => HandleStartAsync => Start processing ProcessPaymentSagaCommand for OrderId: {OrderId}", message.OrderId);

            var payment = mapper.Map<Domain.Entities.Payment>(message);
            payment = payment with { Id = Guid.CreateVersion7() };
            var id = await paymentService.ProcessPaymentAsync(payment);
            logger.LogInformation("ProcessPaymentSagaCommandHandler => HandleStartAsync => Payment processed with ID: {PaymentId} for OrderId: {OrderId}", id, message.OrderId);

            var paymentProcessedEvent = mapper.Map<PaymentProcessedSagaEvent>(payment);
            await Context.PublishWithTracking(paymentProcessedEvent).ThenMarkAsComplete();
            logger.LogInformation("ProcessPaymentSagaCommandHandler => HandleStartAsync => PaymentProcessedSagaEvent published successfully and ProcessPaymentSagaCommand marked as complete for OrderId: {OrderId}", message.OrderId);
        }
        catch (Exception ex)
        {
            await Context.MarkAsFailed<ProcessPaymentSagaCommand>();
            logger.LogError(ex, "ProcessPaymentSagaCommandHandler => HandleStartAsync => Error processing ProcessPaymentSagaCommand for OrderId: {OrderId}", message.OrderId);

            throw new Exception($"ProcessPaymentSagaCommandHandler => HandleStartAsync => Error : {ex.InnerException?.Message ?? ex.Message}", ex);
        }
    }
    public override async Task CompensateStartAsync(ProcessPaymentSagaCommand message)
    {
        try
        {
            logger.LogInformation("ProcessPaymentSagaCommandHandler => CompensateStartAsync => Start compensating ProcessPaymentSagaCommand for OrderId: {OrderId}", message.OrderId);

            var payment = mapper.Map<Domain.Entities.Payment>(message);
            await paymentService.RefundPaymentAsync(payment);
            logger.LogInformation("ProcessPaymentSagaCommandHandler => CompensateStartAsync => Payment with ID: {PaymentId} refunded successfully for OrderId: {OrderId}", payment.Id, message.OrderId);

            await Context.MarkAsCompensated<StockReservedSagaEvent>();
            logger.LogInformation("ProcessPaymentSagaCommandHandler => CompensateStartAsync => Compensation completed successfully for OrderId: {OrderId}", message.OrderId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ProcessPaymentSagaCommandHandler => CompensateStartAsync => Error during compensation of ProcessPaymentSagaCommand for OrderId: {OrderId}", message.OrderId);

            await Context.MarkAsCompensationFailed<StockReservedSagaEvent>();
            logger.LogError("ProcessPaymentSagaCommandHandler => CompensateStartAsync => Error processing ProcessPaymentSagaCommand compensation for OrderId: {OrderId}", message.OrderId);

            throw new Exception($"ProcessPaymentSagaCommandHandler => CompensateStartAsync => Error : {ex.InnerException?.Message ?? ex.Message}", ex);
        }
    }
}