using Lycia.Saga.Handlers;
using Sample.Shared.Messages.Commands;
using Sample.Shared.Messages.Responses;
using Sample.Shared.SagaStates;

namespace Sample.Order.Orchestration.Sequential.Consumer_.Sagas;

public class InventorySagaHandler : 
    CoordinatedSagaHandler<ReserveInventoryCommand, CreateOrderSagaData>
{
    public override async Task HandleAsync(ReserveInventoryCommand message)
    {
        await Context.Send(new ProcessPaymentCommand
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