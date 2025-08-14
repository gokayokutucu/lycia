using Lycia.Saga.Handlers;
using MapsterMapper;
using Microsoft.Extensions.Logging;
using Sample_Net90.Choreography.Application.Interfaces.Services;
using Sample_Net90.Choreography.Domain.Sagas.Payment.ProcessPayment.Events;
using Sample_Net90.Choreography.Domain.Sagas.Shipment.ScheduleShipment.Events;

namespace Sample_Net90.Choreography.Application.Shipment.Commands.Schedule.Handlers;

public sealed class ScheduleShipmentSagaEventHandler (ILogger<ScheduleShipmentSagaEventHandler> logger, IMapper mapper, IDeliveryService deliveryService)
    : ReactiveSagaHandler<PaymentProcessedSagaEvent>
{
    public override async Task HandleAsync(PaymentProcessedSagaEvent message)
    {
        try
        {
            logger.LogInformation("ScheduleShipmentSagaEventHandler => HandleAsync => Start processing PaymentProcessedSagaEvent for OrderId: {OrderId}", message.OrderId);

            //get addresses from db
            var shipment = mapper.Map<Domain.Entities.Shipment>(message);
            
            await deliveryService.ScheduleShipmentAsync(shipment);
            logger.LogInformation("ScheduleShipmentSagaEventHandler => HandleAsync => Shipment scheduled with ID: {ShipmentId} for PaymentId: {PaymentId}", shipment.ShipmentId, message.PaymentId);

            var shipmentScheduledEvent = mapper.Map<ShipmentScheduledSagaEvent>(shipment);
            await Context.PublishWithTracking(shipmentScheduledEvent).ThenMarkAsComplete();
            logger.LogInformation("ScheduleShipmentSagaEventHandler => HandleAsync => ShipmentScheduledSagaEvent published successfully and PaymentProcessedSagaEvent marked as complete for PaymentId: {PaymentId}", message.PaymentId);
        }
        catch (Exception ex)
        {
            await Context.MarkAsFailed<PaymentProcessedSagaEvent>();
            logger.LogError(ex, "ScheduleShipmentSagaEventHandler => HandleAsync => Error processing PaymentProcessedSagaEvent for PaymentId: {PaymentId}", message.PaymentId);
            
            throw new Exception($"ScheduleShipmentSagaEventHandler => HandleAsync => Error : {ex.InnerException?.Message ?? ex.Message}", ex);
        }
    }
    public override async Task CompensateAsync(PaymentProcessedSagaEvent message)
    {
        try
        {
            logger.LogInformation("ScheduleShipmentSagaEventHandler => CompensateAsync => Start compensating for PaymentProcessedSagaEvent with PaymentId: {PaymentId}", message.PaymentId);
            
            var shipment = mapper.Map<Domain.Entities.Shipment>(message);
            await deliveryService.CancelShipmentAsync(shipment);
            logger.LogInformation("ScheduleShipmentSagaEventHandler => CompensateAsync => Shipment with ID: {ShipmentId} cancelled successfully for PaymentId: {PaymentId}", shipment.ShipmentId, message.PaymentId);
            
            await Context.MarkAsCompensated<PaymentProcessedSagaEvent>();
            logger.LogInformation("ScheduleShipmentSagaEventHandler => CompensateAsync => Compensation completed successfully for PaymentId: {PaymentId}", message.PaymentId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ScheduleShipmentSagaEventHandler => CompensateAsync => Error during compensation of PaymentProcessedSagaEvent for PaymentId: {PaymentId}", message.PaymentId);
            
            await Context.MarkAsCompensationFailed<PaymentProcessedSagaEvent>();
            logger.LogError("ScheduleShipmentSagaEventHandler => CompensateAsync => Error processing PaymentProcessedSagaEvent compensation for PaymentId: {PaymentId}", message.PaymentId);
            
            throw new Exception($"ScheduleShipmentSagaEventHandler => CompensateAsync => Error : {ex.InnerException?.Message ?? ex.Message}", ex);
        }
    }
}