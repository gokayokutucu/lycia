using Lycia.Saga.Handlers;
using Lycia.Saga.Handlers.Abstractions;
using Sample.Shared.Messages.Events;
using Sample.Shared.Services;

namespace Sample.Order.Choreography.Consumer.Sagas;

public class ShippingSagaHandler :
    ReactiveSagaHandler<PaymentSucceededEvent>,
    ISagaCompensationHandler<OrderShippingFailedEvent>
{
    public override async Task HandleAsync(PaymentSucceededEvent evt, CancellationToken cancellationToken = default)
    {
        // Try to ship
        var shipped = ShippingService.TryShip(evt.OrderId);
        if (!shipped)
        {
            // Broadcast failure so *interested* parties can react
            await Context.Publish(new OrderShippingFailedEvent
            {
                OrderId = evt.OrderId,
                ParentMessageId = evt.MessageId
            }, cancellationToken);
            await Context.MarkAsFailed<PaymentSucceededEvent>(cancellationToken);
            return;
        }

        await Context.Publish(new OrderShippedEvent
        {
            OrderId = evt.OrderId,
            ParentMessageId = evt.MessageId
        }, cancellationToken);
        await Context.MarkAsComplete<PaymentSucceededEvent>();
    }

    public Task CompensateAsync(OrderShippingFailedEvent failed, CancellationToken cancellationToken = default)
    {
        // Undo or no-op (often nothing to undo if shipping didn’t happen)
        return Task.CompletedTask;
    }
}