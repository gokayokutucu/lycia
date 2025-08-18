using Lycia.Saga.Handlers;
using Lycia.Tests.Messages;
using Lycia.Tests.SagaStates;

namespace Lycia.Tests.Sagas;

public class ShipOrderSagaHandler : CoordinatedSagaHandler<OrderCreatedEvent, CreateOrderSagaData>
{
    public override async Task HandleAsync(OrderCreatedEvent command, CancellationToken cancellationToken = default)
    {
        try
        {
            // Simulated logic
            const bool stockAvailable = true; // Simulate failure
            
            if (!stockAvailable)
            {
                await Context.MarkAsFailed<OrderCreatedEvent>(cancellationToken);
                return;
            }

            await Context.PublishWithTracking(new OrderShippedEvent
            {
                OrderId = command.OrderId,
                ShipmentTrackId = Guid.NewGuid(),
                ShippedAt = DateTime.UtcNow
            })
                .ThenMarkAsComplete();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"🚨 Shipping failed: {ex.Message}");
            await Context.MarkAsFailed<OrderCreatedEvent>(cancellationToken);
        }
    }
}

// For Unit Testing purposes, we can create a handler that simulates a failure scenario.