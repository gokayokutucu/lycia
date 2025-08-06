using Lycia.Saga.Handlers;
using Sample.Shared.Messages.Commands;
using Sample.Shared.Messages.Responses;
using Sample.Shared.SagaStates;

namespace Sample.Order.Choreography.Consumer.Sagas;

public class InventorySagaHandler : 
    CoordinatedResponsiveSagaHandler<ReserveInventoryCommand, InventoryReservedResponse, CreateOrderSagaData>
{
    public override async Task HandleAsync(ReserveInventoryCommand message)
    {
        await Context.Publish(new InventoryReservedResponse
        {
            OrderId = message.OrderId,
            ParentMessageId = message.MessageId
        });
        await Context.MarkAsComplete<ReserveInventoryCommand>();
    }

    public override Task CompensateAsync(ReserveInventoryCommand message)
    {
        Context.Data.InventoryCompensated = true;
        return Task.CompletedTask;
    }
}