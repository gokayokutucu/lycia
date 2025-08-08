using Lycia.Saga.Handlers;
using Sample.Shared.Messages.Commands;
using Sample.Shared.Messages.Responses;
using Sample.Shared.SagaStates;

namespace Sample.Order.Orchestration.Seq.Consumer.Sagas;
public class InventorySagaHandler : 
    CoordinatedSagaHandler<ReserveInventoryCommand, CreateOrderSagaData>
{
    public override async Task HandleAsync(ReserveInventoryCommand message)
    {
        // Simulate inventory reservation
        var inventoryReserved = true; // Simulate logic
        
        if (!inventoryReserved)
        {
            // Inventory reservation failed
            await Context.MarkAsFailed<ReserveInventoryCommand>();
            return;
        }
        
        // Inventory reserved, proceed to payment
        await Context.Send(new ProcessPaymentCommand
        {
            OrderId = message.OrderId,
            ParentMessageId = message.MessageId
        });
        await Context.MarkAsComplete<ReserveInventoryCommand>();
    }

    public override async Task CompensateAsync(ReserveInventoryCommand message)
    {
        // Compensation logic: release reserved inventory
        Context.Data.InventoryCompensated = true;
        await Context.CompensateAndBubbleUp<ReserveInventoryCommand>();
    }
}