using Lycia.Saga.Handlers;
using Sample.Shared.Messages.Events;
using System.Threading.Tasks;
using Lycia.Saga.Abstractions;
using System; // For Console.WriteLine

namespace Sample.Shared.Messages.Sagas
{
    public class SagaFailureNotificationHandler :
        ReactiveSagaHandler<LyciaSagaFailedEvent, LyciaSagaData>
    {
        public override async Task HandleAsync(LyciaSagaFailedEvent eventData)
        {
            // Simulate sending a saga failure notification
            string adminEmail = "admin@example.com"; // Could be from config
            Console.WriteLine($"CRITICAL NOTIFICATION: Order {SagaData.OrderId} failed at step {eventData.FailedStep}. Reason: {eventData.FailureReason}. Notifying User: {SagaData.UserEmail} and Admin: {adminEmail}");
            // In a real scenario, interact with an email/notification service.

            SagaData.OrderStatus = $"NotificationSent_SagaFailure_{eventData.FailedStep}";

            var notificationSentEvent = new NotificationSentEvent
            {
                OrderId = SagaData.OrderId,
                NotificationType = $"SagaFailure_{eventData.FailedStep}"
            };

            await Context.PublishWithTracking(notificationSentEvent)
                         .ThenMarkAsComplete();
            
            // This handler reacts to a failure event. The saga is already considered "not successful".
            // Depending on the Lycia framework's design, you might mark the saga as faulted or completed
            // if this is the absolute final step in all flows (including error flows).
            // For now, just completing this specific notification step.
            // Context.MarkSagaAsFaulted(eventData.FailureReason); or Context.MarkSagaAsCompleted();
        }
    }
}
