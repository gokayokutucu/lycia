using Lycia.Saga.Handlers;
using Sample.Shared.Messages.Events;

namespace Sample.Order.Choreography.Consumer.Sagas;

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

            await Context.MarkAsComplete<OrderCreatedEvent>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"🚨 Shipping failed: {ex.Message}");
            await Context.MarkAsFailed<OrderCreatedEvent>();
        }
    }
}