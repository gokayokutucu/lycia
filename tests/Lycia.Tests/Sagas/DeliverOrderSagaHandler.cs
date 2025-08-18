using Lycia.Saga.Handlers;
using Lycia.Tests.Messages;
using Lycia.Tests.SagaStates;

namespace Lycia.Tests.Sagas;

public class DeliverOrderSagaHandler : CoordinatedSagaHandler<OrderShippedEvent, CreateOrderSagaData>
{
    public override async Task HandleAsync(OrderShippedEvent command, CancellationToken cancellationToken = default)
    {
        // Simulate delivery logic
        await Context.PublishWithTracking(new OrderDeliveredEvent
        {
            OrderId = command.OrderId
        }, cancellationToken)
            .ThenMarkAsComplete();
    }
}