using Lycia.Saga.Handlers;
using Sample.Shared.Messages.Commands;
using Sample.Shared.Messages.Responses;
using Sample.Shared.SagaStates;

namespace Sample.Order.Orchestration.Consumer.Sagas;

public class ShippingSagaHandler :
    CoordinatedSagaHandler<ShipOrderCommand, CreateOrderSagaData>
{
    public override async Task HandleAsync(ShipOrderCommand message)
    {
        // Shipping logic
        await Context.Publish(new OrderShippedResponse
        {
            OrderId = message.OrderId,
            ParentMessageId = message.MessageId
        });
        await Context.MarkAsComplete<ShipOrderCommand>();
    }

    public override Task CompensateAsync(ShipOrderCommand message)
    {
        Context.Data.ShippingCompensated = true; // Sample flag to indicate compensation
        return Context.CompensateAndBubbleUp<ShipOrderCommand>();
    }
}