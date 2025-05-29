using Lycia.Saga.Abstractions;
using Lycia.Saga.Handlers;
using Sample.Shared.Messages.Events;

namespace Sample.Shared.Messages.Sagas;

public class ShipOrderForCompensationSagaHandler :
    ReactiveSagaHandler<OrderCreatedEvent>,
    ISagaCompensationHandler<OrderCreatedEvent>
{
    /// <summary>
    /// For test purposes, we can check if the compensation was called.
    /// </summary>
    public bool CompensateCalled { get; set; }

    public override async Task HandleAsync(OrderCreatedEvent command)
    {
        try
        {
            // Simulated logic
            const bool stockAvailable = false; // Simulate failure

            if (stockAvailable)
            {
                await Context.PublishWithTracking(new OrderShippedEvent
                    {
                        OrderId = command.OrderId,
                        ShipmentTrackId = Guid.NewGuid(),
                        ShippedAt = DateTime.UtcNow
                    })
                    .ThenMarkAsComplete();
                return;
            }

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
            await Context.Publish(new OrderShippingFailedEvent
            {
                OrderId = message.OrderId
            });
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