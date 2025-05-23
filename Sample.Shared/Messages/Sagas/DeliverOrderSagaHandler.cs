using Lycia.Saga.Handlers;
using Sample.Shared.Messages.Events;

public class DeliverOrderSagaHandler : ReactiveSagaHandler<OrderShippedEvent>
{
    public override async Task HandleStartAsync(OrderShippedEvent command)
    {
        // Simulate delivery logic
        await Context.Publish(new OrderDeliveredEvent
        {
            OrderId = command.OrderId
        });

        await Context.MarkAsComplete<OrderShippedEvent>();
    }
}