using Lycia.Saga.Handlers;
using Sample.Shared.Messages.Events;

namespace Lycia.Tests.Sagas;

public class DeliverOrderSagaHandler : ReactiveSagaHandler<OrderShippedEvent>
{
    public override async Task HandleAsync(OrderShippedEvent command)
    {
        // Simulate delivery logic
        await Context.PublishWithTracking(new OrderDeliveredEvent
        {
            OrderId = command.OrderId
        }).ThenMarkAsComplete();
    }
}