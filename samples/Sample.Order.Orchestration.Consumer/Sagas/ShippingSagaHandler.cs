using Lycia.Saga.Handlers;
using Sample.Shared.Messages.Commands;
using Sample.Shared.Messages.Responses;
using Sample.Shared.SagaStates;

namespace Sample.Order.Orchestration.Consumer.Sagas;

public class ShippingSagaHandler :
    CoordinatedSagaHandler<ShipOrderCommand, CreateOrderSagaData>
{
    public override async Task HandleAsync(ShipOrderCommand message, CancellationToken cancellationToken = default)
    {
        // Shipping logic
        await Context.Publish(new OrderShippedResponse
        {
            OrderId = message.OrderId,
            ParentMessageId = message.MessageId
        }, cancellationToken);
        await Context.MarkAsComplete<ShipOrderCommand>(cancellationToken);
    }

    public override Task CompensateAsync(ShipOrderCommand message, CancellationToken cancellationToken = default)
    {
        Context.Data.ShippingCompensated = true; // Sample flag to indicate compensation
        return Context.CompensateAndBubbleUp<ShipOrderCommand>(cancellationToken);
    }
}