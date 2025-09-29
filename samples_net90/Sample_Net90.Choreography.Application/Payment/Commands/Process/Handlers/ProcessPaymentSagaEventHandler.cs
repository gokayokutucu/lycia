using Lycia.Handlers;
using MapsterMapper;
using Microsoft.Extensions.Logging;
using Sample_Net90.Choreography.Application.Interfaces.Repositories;
using Sample_Net90.Choreography.Application.Interfaces.Services;
using Sample_Net90.Choreography.Domain.Sagas.Payment.ProcessPayment.Events;
using Sample_Net90.Choreography.Domain.Sagas.Stock.ReserveStock.Events;

namespace Sample_Net90.Choreography.Application.Payment.Commands.Process.Handlers;

public sealed class ProcessPaymentSagaEventHandler(ILogger<ProcessPaymentSagaEventHandler> logger, IMapper mapper, IPaymentService paymentService, IStockRepository stockRepository)
    : ReactiveSagaHandler<StockReservedSagaEvent>
{
    public override async Task HandleAsync(StockReservedSagaEvent message, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            logger.LogInformation("ProcessPaymentSagaEventHandler => HandleAsync => Start processing StockReservedSagaEvent for ReservationId: {ReservationId}", message.ReservationId);

            if (!await stockRepository.IsReservedAsync(message.ReservationId))
            {
                logger.LogWarning("ProcessPaymentSagaEventHandler => HandleAsync => Reservation with ID: {ReservationId} does not exist. Cannot process payment.", message.ReservationId);
                throw new InvalidOperationException($"Reservation with ID: {message.ReservationId} does not exist.");
            }

            var payment = mapper.Map<Domain.Entities.Payment>(message);

            await paymentService.ProcessPaymentAsync(payment);
            logger.LogInformation("ProcessPaymentSagaEventHandler => HandleAsync => Payment processed with ID: {PaymentId} for ReservationId: {ReservationId}", payment.PaymentId, message.ReservationId);

            var paymentProcessedEvent = mapper.Map<PaymentProcessedSagaEvent>(payment);
            await Context.PublishWithTracking(paymentProcessedEvent).ThenMarkAsComplete();
            logger.LogInformation("ProcessPaymentSagaEventHandler => HandleAsync => PaymentProcessedSagaEvent published successfully and StockReservedSagaEvent marked as complete for ReservationId: {ReservationId}", message.ReservationId);
        }
        catch (Exception ex)
        {
            await Context.MarkAsFailed<StockReservedSagaEvent>();
            logger.LogError(ex, "ProcessPaymentSagaEventHandler => HandleAsync => Error processing StockReservedSagaEvent for ReservationId: {ReservationId}", message.ReservationId);

            throw new Exception($"ProcessPaymentSagaEventHandler => HandleAsync => Error : {ex.InnerException?.Message ?? ex.Message}", ex);
        }
    }
    public override async Task CompensateAsync(StockReservedSagaEvent message, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            logger.LogInformation("ProcessPaymentSagaEventHandler => CompensateAsync => Start compensating StockReservedSagaEvent for ReservationId: {ReservationId}", message.ReservationId);

            var payment = mapper.Map<Domain.Entities.Payment>(message);
            await paymentService.RefundPaymentAsync(payment);
            logger.LogInformation("ProcessPaymentSagaEventHandler => CompensateAsync => Payment with ID: {PaymentId} refunded successfully for ReservationId: {ReservationId}", payment.PaymentId, message.ReservationId);

            await Context.MarkAsCompensated<StockReservedSagaEvent>();
            logger.LogInformation("ProcessPaymentSagaEventHandler => CompensateAsync => Compensation completed successfully for ReservationId: {ReservationId}", message.ReservationId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ProcessPaymentSagaEventHandler => CompensateAsync => Error during compensation of StockReservedSagaEvent for ReservationId: {ReservationId}", message.ReservationId);

            await Context.MarkAsCompensationFailed<StockReservedSagaEvent>();
            logger.LogError("ProcessPaymentSagaEventHandler => CompensateAsync => Error processing StockReservedSagaEvent compensation for ReservationId: {ReservationId}", message.ReservationId);

            throw new Exception($"ProcessPaymentSagaEventHandler => CompensateAsync => Error : {ex.InnerException?.Message ?? ex.Message}", ex);
        }
    }
}