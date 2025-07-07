using Lycia.Saga.Abstractions;
using Lycia.Saga.Handlers;
using Sample.Shared.Messages.Events;

namespace Sample.Shared.Messages.Sagas;

public class ShipOrderForCompensationSagaHandler :
    ReactiveSagaHandler<OrderCreatedEvent>
{
    /// <summary>
    /// For test purposes, we can check if the compensation was called.
    /// </summary>
    public bool CompensateCalled { get; set; }

    public override async Task HandleAsync(OrderCreatedEvent @event)
    {
        try
        {
            // Simulated logic
            const bool stockAvailable = false; // Simulate failure

            if (stockAvailable)
            {
                await Context.PublishWithTracking(new OrderShippedEvent
                    {
                        OrderId = @event.OrderId,
                        ShipmentTrackId = Guid.NewGuid(),
                        ShippedAt = DateTime.UtcNow
                    })
                    .ThenMarkAsComplete();
                return;
            }
            
            //@event.TotalPrice = stockAvailable ? @event.TotalPrice : 0;

            await Context.MarkAsFailed<OrderCreatedEvent>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"🚨 Shipping failed: {ex.Message}");
            await Context.MarkAsFailed<OrderCreatedEvent>();
        }
    }

    public override async Task CompensateAsync(OrderCreatedEvent message)
    {
        try
        {
            CompensateCalled = true;
            if(message.TotalPrice <= 0)
            {
                throw new InvalidOperationException("Total price must be greater than zero for compensation.");
            }
            
            await Context.MarkAsCompensated<OrderCreatedEvent>();
        }
        catch (Exception ex)
        {
            // Log, notify, halt chain, etc.
            Console.WriteLine($"❌ Compensation failed: {ex.Message}");

            await Context.MarkAsCompensationFailed<OrderCreatedEvent>();
            // Optionally: rethrow or store for manual retry
            //throw; // Or suppress and log for retry system
        }
    }
}