using Lycia.Saga.Handlers;
using Lycia.Saga.Handlers.Abstractions;
using Sample.Shared.Messages.Events;

namespace Sample.Order.Choreography.Consumer.Sagas;

public class InventorySagaHandler :
    ReactiveSagaHandler<OrderCreatedEvent>,
    ISagaCompensationHandler<PaymentFailedEvent>
{
    public override async Task HandleAsync(OrderCreatedEvent evt, CancellationToken cancellationToken = default)
    {
        // Reserve inventory
        await Context.Publish(new InventoryReservedEvent
        {
            OrderId = evt.OrderId,
            ParentMessageId = evt.MessageId
        }, cancellationToken);
        await Context.MarkAsComplete<OrderCreatedEvent>(cancellationToken);
    }

    public Task CompensateAsync(PaymentFailedEvent failed, CancellationToken cancellationToken = default)
    {
        // Release reserved stock
        // Optionally publish InventoryReleasedEvent
        return Task.CompletedTask;
    }
}