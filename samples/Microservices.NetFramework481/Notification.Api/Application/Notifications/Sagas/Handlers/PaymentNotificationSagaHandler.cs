using Lycia.Saga.Messaging.Handlers;
using Microsoft.Extensions.Logging;
using Sample.Notification.NetFramework481.Application.Interfaces;
using Sample.Notification.NetFramework481.Domain.Notifications;
using Shared.Contracts.Events.Notification;
using Shared.Contracts.Events.Payment;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sample.Notification.NetFramework481.Application.Notifications.Sagas.Handlers;

public sealed class PaymentNotificationSagaHandler(
    INotificationRepository notificationRepository,
    ILogger<PaymentNotificationSagaHandler> logger)
: ReactiveSagaHandler<PaymentProcessedEvent>
{
    public override async Task HandleAsync(PaymentProcessedEvent message, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            logger.LogInformation("Sending payment EMAIL notification for order: {OrderId} to {Email}",
                message.OrderId, message.CustomerEmail);

            var emailMessage = $"Dear {message.CustomerName},\n\n" +
                              $"Your payment of ${message.Amount:F2} has been processed successfully for Order #{message.OrderId}.\n" +
                              $"Transaction ID: {message.TransactionId}\n\n" +
                              $"We will notify you once your order is shipped.";

            var notification = new Domain.Notifications.Notification
            {
                Recipient = message.CustomerEmail,
                Type = NotificationType.Email,
                Subject = "Payment Confirmed - Order Processing",
                Message = emailMessage,
                Status = NotificationStatus.Pending,
                RelatedEntityId = message.OrderId,
                RelatedEntityType = "Order"
            };

            await notificationRepository.SaveAsync(notification, cancellationToken);
            await notificationRepository.SendNotificationAsync(notification.Id, cancellationToken);

            logger.LogInformation("Payment email notification sent to {Email}", message.CustomerEmail);

            await Context.MarkAsComplete<PaymentProcessedEvent>();
        }
        catch (OperationCanceledException ex)
        {
            await Context.Publish(new CustomerNotifiedFailedEvent(ex.Message) { OrderId = message.OrderId }, cancellationToken);
            await Context.MarkAsCancelled<PaymentProcessedEvent>(ex);
        }
        catch (Exception ex)
        {
            await Context.Publish(new CustomerNotifiedFailedEvent(ex.Message) { OrderId = message.OrderId }, cancellationToken);
            await Context.MarkAsFailed<PaymentProcessedEvent>(ex, cancellationToken);
        }
    }
}
