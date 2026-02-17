using Lycia.Saga.Abstractions.Handlers;
using Lycia.Saga.Messaging.Handlers;
using Microsoft.Extensions.Logging;
using Sample.Delivery.NetFramework481.Application.Interfaces;
using Sample.Delivery.NetFramework481.Domain.Deliveries;
using Shared.Contracts.Events.Delivery;
using Shared.Contracts.Events.Notification;
using Shared.Contracts.Events.Payment;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sample.Delivery.NetFramework481.Application.Deliveries.Sagas.Handlers;

public sealed class ScheduleShipmentSagaHandler(
    IDeliveryRepository deliveryRepository,
    ILogger<ScheduleShipmentSagaHandler> logger)
: ReactiveSagaHandler<PaymentProcessedEvent>
, ISagaCompensationHandler<CustomerNotifiedFailedEvent>
{
    public override async Task HandleAsync(PaymentProcessedEvent message, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            logger.LogInformation($"Creating delivery for order: {message.OrderId}");

            var delivery = new Domain.Deliveries.Delivery
            {
                OrderId = message.OrderId,
                CustomerName = message.CustomerName,
                ShippingStreet = message.ShippingStreet,
                ShippingCity = message.ShippingCity,
                ShippingState = message.ShippingState,
                ShippingZipCode = message.ShippingZipCode,
                ShippingCountry = message.ShippingCountry,
                Status = DeliveryStatus.Pending,
                TrackingNumber = Guid.NewGuid().ToString("N").Substring(0, 16).ToUpper(),
                DeliveryDate = DateTime.UtcNow.AddDays(3)
            };

            await deliveryRepository.SaveAsync(delivery, cancellationToken);

            logger.LogInformation("Delivery created: {DeliveryId}, Tracking: {TrackingNumber}",
                delivery.Id, delivery.TrackingNumber);

            var shipmentScheduledEvent = new ShipmentScheduledEvent
            {
                ShipmentId = delivery.Id,
                OrderId = message.OrderId,
                TrackingNumber = delivery.TrackingNumber,
                ScheduledDate = delivery.DeliveryDate ?? DateTime.UtcNow.AddDays(3),
                CustomerName = message.CustomerName,
                CustomerPhone = message.CustomerPhone
            };

            await Context.Publish(shipmentScheduledEvent, cancellationToken);
            await Context.MarkAsComplete<PaymentProcessedEvent>();
        }
        catch (OperationCanceledException ex)
        {
            await Context.Publish(new ShipmentScheduledFailedEvent(ex.Message) { OrderId = message.OrderId }, cancellationToken);
            await Context.MarkAsCancelled<PaymentProcessedEvent>(ex);
        }
        catch (Exception ex)
        {
            await Context.Publish(new ShipmentScheduledFailedEvent(ex.Message) { OrderId = message.OrderId }, cancellationToken);
            await Context.MarkAsFailed<PaymentProcessedEvent>(ex, cancellationToken);
        }
    }

    public async Task CompensateAsync(CustomerNotifiedFailedEvent message, CancellationToken cancellationToken = default)
    {
        try
        {
            var delivery = await deliveryRepository.GetByOrderIdAsync(message.OrderId, cancellationToken);
            if (delivery != null)
                await deliveryRepository.UpdateStatusAsync(delivery.Id, DeliveryStatus.Cancelled, cancellationToken);
            await Context.MarkAsCompensated<CustomerNotifiedFailedEvent>();
        }
        catch (Exception ex)
        {
            await Context.MarkAsCompensationFailed<CustomerNotifiedFailedEvent>(ex);
        }
    }
}
