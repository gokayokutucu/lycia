using Lycia.Saga.Handlers;
using Sample.Shared.Messages.Events;

namespace Sample.Shared.Messages.Sagas;

public class ShipOrderSagaHandler : ReactiveSagaHandler<OrderCreatedEvent>
{
    public override async Task HandleAsync(OrderCreatedEvent command)
    {
        try
        {
            // Simulated logic
            const bool stockAvailable = true; // Simulate failure
            
            if (!stockAvailable)
            {
                await Context.MarkAsFailed<OrderCreatedEvent>();
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
            await Context.MarkAsFailed<OrderCreatedEvent>();
        }
    }
}

// For Unit Testing purposes, we can create a handler that simulates a failure scenario.