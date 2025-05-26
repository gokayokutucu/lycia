using Lycia.Saga.Handlers;
using Sample.Shared.Messages.Events;

namespace Sample.Shared.Messages.Sagas;

public class ShipOrderForCompensationSagaHandler : ReactiveSagaHandler<OrderCreatedEvent>
{
    public override async Task HandleStartAsync(OrderCreatedEvent command)
    {
        try
        {
            // Simulated logic
            const bool stockAvailable = false; // Simulate failure
            
            if (!stockAvailable)
            {
                await Context.MarkAsFailed<OrderCreatedEvent>();
                return;
            }

            await Context.Publish(new OrderShippedEvent
            {
                OrderId = command.OrderId,
                ShipmentTrackId = Guid.NewGuid(),
                ShippedAt = DateTime.UtcNow
            });

            await Context.MarkAsComplete<OrderCreatedEvent>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"🚨 Shipping failed: {ex.Message}");
            await Context.MarkAsFailed<OrderCreatedEvent>();
        }
    }
}