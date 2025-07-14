using Lycia.Saga.Handlers;
using Sample_Net48.Shared.Messages.Events;
using System.Threading.Tasks;

namespace Sample_Net48.Shared.Messages.Sagas
{
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
}