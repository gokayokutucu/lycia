using Lycia.Saga.Handlers;
using Sample.Shared.Messages.Events;
using System.Threading.Tasks;
using Lycia.Saga.Abstractions;
using System; // For Console.WriteLine or other simulation logic
using System.Collections.Generic; // For List<Guid>
using System.Linq; // For .Select
using Sample.Shared.Messages.Commands; // For OrderItem if used directly

namespace Sample.Shared.Messages.Sagas
{
    public class InventorySagaHandler :
        ReactiveSagaHandler<LyciaSagaStartedEvent, LyciaSagaData>,
        ISagaCompensationHandler<PaymentFailedEvent, LyciaSagaData>,
        ISagaCompensationHandler<ShipmentFailedEvent, LyciaSagaData>
    {
        public override async Task HandleAsync(LyciaSagaStartedEvent eventData)
        {
            Console.WriteLine($"Attempting inventory update for OrderId: {SagaData.OrderId}");

            // Simulate failure condition
            if (SagaData.OrderId.ToString().Contains("bad"))
            {
                SagaData.OrderStatus = "InventoryUpdateFailed";
                SagaData.FailureReason = "Simulated inventory update failure (e.g., item out of stock)";
                Console.WriteLine($"{SagaData.FailureReason} for OrderId: {SagaData.OrderId}");

                var failedProductIds = SagaData.Items?.Select(item => item.ProductId).ToList() ?? new List<Guid>();
                if (!failedProductIds.Any() && SagaData.Items != null && SagaData.Items.Any()) // if Items is not null but ProductIds were null
                {
                    // Fallback if ProductId was somehow null for some items, add a dummy Guid
                    failedProductIds.Add(Guid.NewGuid()); 
                }
                else if (!failedProductIds.Any())
                {
                    failedProductIds.Add(Guid.NewGuid()); // Default dummy if no items
                }


                var inventoryUpdateFailedEvent = new InventoryUpdateFailedEvent
                {
                    OrderId = SagaData.OrderId,
                    FailedProductIds = failedProductIds,
                    Reason = SagaData.FailureReason
                };
                await Context.PublishWithTracking(inventoryUpdateFailedEvent)
                             .ThenMarkAsComplete(); // This step is "complete" in that it did its work, which was to fail.
            }
            else
            {
                // Simulate inventory update success logic
                Console.WriteLine($"Successfully updated inventory for OrderId: {SagaData.OrderId}");
                SagaData.OrderStatus = "InventoryUpdated";

                var inventoryUpdatedEvent = new InventoryUpdatedEvent
                {
                    OrderId = SagaData.OrderId
                };

                await Context.PublishWithTracking(inventoryUpdatedEvent)
                             .ThenMarkAsComplete();
            }
        }

        public async Task CompensateAsync(PaymentFailedEvent eventData, ISagaContext<PaymentFailedEvent, LyciaSagaData> context)
        {
            try
            {
                // Simulate inventory compensation logic due to payment failure
                Console.WriteLine($"Compensating inventory for OrderId: {context.SagaData.OrderId} due to PaymentFailure: {eventData.Reason}");
                // In a real scenario, interact with an inventory service to restock items.
                // if (someConditionToSimulateCompensationFailure) throw new Exception("Simulated inventory compensation failure.");

                context.SagaData.OrderStatus = "InventoryCompensatedAfterPaymentFailure";
                context.SagaData.FailureReason = $"PaymentFailed: {eventData.Reason}"; // Keep original failure, or append if needed

                await context.MarkAsCompensated<LyciaSagaStartedEvent>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"CRITICAL: Inventory compensation failed for OrderId: {context.SagaData.OrderId} after PaymentFailure. Error: {ex.Message}");
                context.SagaData.OrderStatus = "InventoryCompensationFailedAfterPaymentFailure";
                context.SagaData.FailureReason = $"Critical: Inventory compensation failed after payment failure. Original reason: {eventData.Reason}, Compensation error: {ex.Message}";

                var lyciaSagaFailedEvent = new LyciaSagaFailedEvent
                {
                    OrderId = context.SagaData.OrderId,
                    FailureReason = context.SagaData.FailureReason,
                    FailedStep = "InventoryCompensationAfterPaymentFailure"
                };

                await context.PublishWithTracking(lyciaSagaFailedEvent)
                             .ThenMarkAsFaulted<LyciaSagaStartedEvent>(); // Mark the original initiating step's path as terminally faulted
                // MarkAsCompensationFailed might be redundant if ThenMarkAsFaulted achieves the terminal state for this path.
                // Check Lycia documentation if specific order or combination is required.
                // For now, assuming ThenMarkAsFaulted is sufficient to indicate this branch of the saga is irrecoverably failed.
                // await context.MarkAsCompensationFailed<LyciaSagaStartedEvent>(); // Potentially redundant
            }
        }

        public async Task CompensateAsync(ShipmentFailedEvent eventData, ISagaContext<ShipmentFailedEvent, LyciaSagaData> context)
        {
            try
            {
                // Simulate inventory compensation logic due to shipment failure
                Console.WriteLine($"Compensating inventory for OrderId: {context.SagaData.OrderId} due to ShipmentFailure: {eventData.Reason}");
                // In a real scenario, interact with an inventory service to restock items.
                // if (someConditionToSimulateCompensationFailure) throw new Exception("Simulated inventory compensation failure.");

                context.SagaData.OrderStatus = "InventoryCompensatedAfterShipmentFailure";
                context.SagaData.FailureReason = $"ShipmentFailed: {eventData.Reason}"; // Keep original failure, or append

                await context.MarkAsCompensated<LyciaSagaStartedEvent>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"CRITICAL: Inventory compensation failed for OrderId: {context.SagaData.OrderId} after ShipmentFailure. Error: {ex.Message}");
                context.SagaData.OrderStatus = "InventoryCompensationFailedAfterShipmentFailure";
                context.SagaData.FailureReason = $"Critical: Inventory compensation failed after shipment failure. Original reason: {eventData.Reason}, Compensation error: {ex.Message}";
                
                var lyciaSagaFailedEvent = new LyciaSagaFailedEvent
                {
                    OrderId = context.SagaData.OrderId,
                    FailureReason = context.SagaData.FailureReason,
                    FailedStep = "InventoryCompensationAfterShipmentFailure"
                };

                await context.PublishWithTracking(lyciaSagaFailedEvent)
                             .ThenMarkAsFaulted<LyciaSagaStartedEvent>();
                // await context.MarkAsCompensationFailed<LyciaSagaStartedEvent>(); // Potentially redundant
            }
        }
    }
}
