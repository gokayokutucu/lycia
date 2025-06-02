using Lycia.Saga.Handlers;
using Sample.Shared.Messages.Events;
using System.Threading.Tasks;
using Lycia.Saga.Abstractions;
using System; // For Guid.NewGuid, DateTime.UtcNow, Console.WriteLine

namespace Sample.Shared.Messages.Sagas
{
    public class PaymentSagaHandler :
        ReactiveSagaHandler<InventoryUpdatedEvent, LyciaSagaData>,
        ISagaCompensationHandler<ShipmentFailedEvent, LyciaSagaData>
    {
        public override async Task HandleAsync(InventoryUpdatedEvent eventData)
        {
            Console.WriteLine($"Attempting payment processing for OrderId: {SagaData.OrderId}");

            // Simulate failure condition based on CardDetails
            if (SagaData.CardDetails != null && SagaData.CardDetails.Contains("fail"))
            {
                SagaData.OrderStatus = "PaymentFailed";
                SagaData.FailureReason = "Simulated payment failure (e.g., insufficient funds or card declined)";
                Console.WriteLine($"{SagaData.FailureReason} for OrderId: {SagaData.OrderId}");

                var paymentFailedEvent = new PaymentFailedEvent
                {
                    OrderId = SagaData.OrderId,
                    Reason = SagaData.FailureReason
                };
                // This event is expected to trigger compensation in InventorySagaHandler
                await Context.PublishWithTracking(paymentFailedEvent)
                             .ThenMarkAsComplete();
            }
            else
            {
                // Simulate successful payment processing
                Console.WriteLine($"Successfully processed payment for OrderId: {SagaData.OrderId} with CardDetails: {SagaData.CardDetails}");
                SagaData.PaymentId = Guid.NewGuid();
                SagaData.OrderStatus = "PaymentProcessed";

                var paymentProcessedEvent = new PaymentProcessedEvent
                {
                    OrderId = SagaData.OrderId,
                    PaymentId = SagaData.PaymentId,
                    PaymentDate = DateTime.UtcNow
                };

                await Context.PublishWithTracking(paymentProcessedEvent)
                             .ThenMarkAsComplete();
            }
        }

        public async Task CompensateAsync(ShipmentFailedEvent eventData, ISagaContext<ShipmentFailedEvent, LyciaSagaData> context)
        {
            try
            {
                // Simulate payment refund logic due to shipment failure
                Console.WriteLine($"Refunding payment {context.SagaData.PaymentId} for OrderId: {context.SagaData.OrderId} due to ShipmentFailure: {eventData.Reason}");
                // In a real scenario, interact with a payment gateway to issue a refund.
                // if (someConditionToSimulateRefundFailure) throw new Exception("Simulated payment refund failure.");

                context.SagaData.OrderStatus = "PaymentRefundedAfterShipmentFailure";
                context.SagaData.FailureReason = $"ShipmentFailed, initiating refund for PaymentId {context.SagaData.PaymentId}: {eventData.Reason}";

                await context.MarkAsCompensated<InventoryUpdatedEvent>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"CRITICAL: Payment refund failed for OrderId: {context.SagaData.OrderId} after ShipmentFailure. Error: {ex.Message}");
                context.SagaData.OrderStatus = "PaymentRefundFailedAfterShipmentFailure";
                context.SagaData.FailureReason = $"Critical: Payment refund failed after shipment failure. Original reason: {eventData.Reason}, Compensation error: {ex.Message}";

                var lyciaSagaFailedEvent = new LyciaSagaFailedEvent
                {
                    OrderId = context.SagaData.OrderId,
                    FailureReason = context.SagaData.FailureReason,
                    FailedStep = "PaymentRefundFailedAfterShipmentFailure"
                };

                await context.PublishWithTracking(lyciaSagaFailedEvent)
                             .ThenMarkAsFaulted<InventoryUpdatedEvent>(); // Mark the original step (InventoryUpdatedEvent) as terminally faulted
                // await context.MarkAsCompensationFailed<InventoryUpdatedEvent>(); // Potentially redundant
            }
        }
    }
}
