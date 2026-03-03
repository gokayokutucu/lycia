using Lycia.Saga.Messaging.Handlers;
using Microsoft.Extensions.Logging;
using Sample.Notification.NetFramework481.Application.Interfaces;
using Sample.Notification.NetFramework481.Domain.Notifications;
using Shared.Contracts.Events.Delivery;
using Shared.Contracts.Events.Notification;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sample.Notification.NetFramework481.Application.Notifications.Sagas.Handlers;

public sealed class DeliveryNotificationSagaHandler(
    INotificationRepository notificationRepository,
    ILogger<DeliveryNotificationSagaHandler> logger)
: ReactiveSagaHandler<ShipmentScheduledEvent>
{
    public override async Task HandleAsync(ShipmentScheduledEvent message, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            logger.LogInformation("Sending delivery SMS notification for order: {OrderId} to {Phone}",
                message.OrderId, message.CustomerPhone);

            var smsMessage = $"Hi {message.CustomerName}! Your order #{message.OrderId} has been shipped. " +
                            $"Tracking: {message.TrackingNumber}. Est. delivery: {message.ScheduledDate:MMM dd}";

            var notification = new Domain.Notifications.Notification
            {
                Recipient = message.CustomerPhone,
                Type = NotificationType.SMS,
                Subject = "Order Shipped",
                Message = smsMessage,
                Status = NotificationStatus.Pending,
                RelatedEntityId = message.OrderId,
                RelatedEntityType = "Delivery"
            };

            await notificationRepository.SaveAsync(notification, cancellationToken);
            await notificationRepository.SendNotificationAsync(notification.Id, cancellationToken);

            logger.LogInformation("Delivery SMS notification sent to {Phone}", message.CustomerPhone);

            await Context.MarkAsComplete<ShipmentScheduledEvent>();
        }
        catch (OperationCanceledException ex)
        {
            await Context.Publish(new CustomerNotifiedFailedEvent(ex.Message) { OrderId = message.OrderId }, cancellationToken);
            await Context.MarkAsCancelled<ShipmentScheduledEvent>(ex);
        }
        catch (Exception ex)
        {
            await Context.Publish(new CustomerNotifiedFailedEvent(ex.Message) { OrderId = message.OrderId }, cancellationToken);
            await Context.MarkAsFailed<ShipmentScheduledEvent>(ex, cancellationToken);
        }
    }
}
