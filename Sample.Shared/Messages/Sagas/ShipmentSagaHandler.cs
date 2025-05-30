using Lycia.Saga.Handlers;
using Sample.Shared.Messages.Events;
using System.Threading.Tasks;
using Lycia.Saga.Abstractions;
using System; // For Guid.NewGuid, DateTime.UtcNow, Console.WriteLine

namespace Sample.Shared.Messages.Sagas
{
    public class ShipmentSagaHandler :
        ReactiveSagaHandler<PaymentProcessedEvent, LyciaSagaData>
    {
        public override async Task HandleAsync(PaymentProcessedEvent eventData)
        {
            Console.WriteLine($"Attempting shipment dispatch for OrderId: {SagaData.OrderId}");

            // Simulate failure condition based on ShippingAddress
            if (SagaData.ShippingAddress != null && SagaData.ShippingAddress.Contains("invalid"))
            {
                SagaData.OrderStatus = "ShipmentFailed";
                SagaData.FailureReason = "Simulated shipment failure (e.g., invalid address)";
                Console.WriteLine($"{SagaData.FailureReason} for OrderId: {SagaData.OrderId}");

                var shipmentFailedEvent = new ShipmentFailedEvent
                {
                    OrderId = SagaData.OrderId,
                    Reason = SagaData.FailureReason
                };
                // This event is expected to trigger compensation in InventorySagaHandler and PaymentSagaHandler
                await Context.PublishWithTracking(shipmentFailedEvent)
                             .ThenMarkAsComplete();
            }
            else
            {
                // Simulate successful shipment dispatch
                Console.WriteLine($"Successfully dispatched shipment for OrderId: {SagaData.OrderId} to Address: {SagaData.ShippingAddress}");
                SagaData.ShipmentTrackingNumber = $"TRK-{Guid.NewGuid().ToString().Substring(0, 8).ToUpper()}";
                SagaData.OrderStatus = "ShipmentDispatched";

                var shipmentDispatchedEvent = new ShipmentDispatchedEvent
                {
                    OrderId = SagaData.OrderId,
                    TrackingNumber = SagaData.ShipmentTrackingNumber,
                    DispatchDate = DateTime.UtcNow
                };

                await Context.PublishWithTracking(shipmentDispatchedEvent)
                             .ThenMarkAsComplete();
            }
        }
    }
}
