using Lycia.Saga.Handlers;
using Sample.Shared.Messages.Events;
using System.Threading.Tasks;
using Lycia.Saga.Abstractions;
using System; // For Console.WriteLine

namespace Sample.Shared.Messages.Sagas
{
    public class OrderConfirmationNotificationSagaHandler :
        ReactiveSagaHandler<ShipmentDispatchedEvent, LyciaSagaData>
    {
        public override async Task HandleAsync(ShipmentDispatchedEvent eventData)
        {
            // Simulate sending an order confirmation notification
            Console.WriteLine($"Notification: Order {SagaData.OrderId} has been shipped. Tracking: {eventData.TrackingNumber}. Email to: {SagaData.UserEmail}");
            // In a real scenario, interact with an email/notification service.

            SagaData.OrderStatus = "NotificationSent_OrderConfirmed";

            var notificationSentEvent = new NotificationSentEvent
            {
                OrderId = SagaData.OrderId,
                NotificationType = "OrderConfirmed" // Or "ShipmentConfirmation"
            };

            await Context.PublishWithTracking(notificationSentEvent)
                         .ThenMarkAsComplete();
            
            // Optionally, you might consider this the final step of the saga for the happy path.
            // If so, you could mark the saga as completed.
            // Context.MarkSagaAsCompleted(); 
            // However, this depends on whether other handlers might react to NotificationSentEvent
            // or if there are other parallel flows. For now, just completing the step.
        }
    }
}
