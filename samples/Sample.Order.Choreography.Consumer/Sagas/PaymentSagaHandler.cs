using Lycia.Saga.Handlers;
using Sample.Shared.Messages.Events;
using Sample.Shared.Services;

namespace Sample.Order.Choreography.Consumer.Sagas;

public class PaymentSagaHandler : ReactiveSagaHandler<InventoryReservedEvent>
{
    public override async Task HandleAsync(InventoryReservedEvent evt, CancellationToken cancellationToken = default)
    {
        var ok = PaymentService.SimulatePayment();
        if (!ok)
        {
            await Context.Publish(new PaymentFailedEvent
            {
                OrderId = evt.OrderId,
                ParentMessageId = evt.MessageId
            }, cancellationToken);

            // Mark only this step as failed (step logging/metrics)
            await Context.MarkAsFailed<InventoryReservedEvent>(cancellationToken);
            return;
        }

        await Context.Publish(new PaymentSucceededEvent
        {
            OrderId = evt.OrderId,
            ParentMessageId = evt.MessageId
        }, cancellationToken);
        await Context.MarkAsComplete<InventoryReservedEvent>();
    }
}