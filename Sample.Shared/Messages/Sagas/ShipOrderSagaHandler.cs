using Lycia.Messaging;
using Lycia.Saga.Handlers;
using Sample.Shared.Messages.Events;

namespace Sample.Shared.Messages.Sagas;

public class ShipOrderSagaHandler : ReactiveSagaHandler<OrderCreatedEvent>
{
    public override async Task HandleStartAsync(OrderCreatedEvent command)
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
public class ShipOrderForCompensationSagaHandler : 
    ReactiveSagaHandler<OrderCreatedEvent>,
    ISagaCompensationHandler<OrderCreatedEvent>
{
    /// <summary>
    /// For test purposes, we can check if the compensation was called.
    /// </summary>
    public bool CompensateCalled { get; set; }
    
    public override async Task HandleStartAsync(OrderCreatedEvent command)
    {
        try
        {
            await Context.MarkAsFailed<OrderCreatedEvent>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"🚨 Shipping failed: {ex.Message}");
            await Context.MarkAsFailed<OrderCreatedEvent>();
        }
    }

    public async Task CompensateAsync(OrderCreatedEvent message)
    {
        try
        {
            CompensateCalled = true;
            // Compensation logic
            await Context.MarkAsCompensated<OrderCreatedEvent>();
        }
        catch (Exception ex)
        {
            // Log, notify, halt chain, etc.
            Console.WriteLine($"❌ Compensation failed: {ex.Message}");
            
            await Context.MarkAsCompensationFailed<OrderCreatedEvent>();
            // Optionally: rethrow or store for manual retry
            throw; // Or suppress and log for retry system
        }
    }
}