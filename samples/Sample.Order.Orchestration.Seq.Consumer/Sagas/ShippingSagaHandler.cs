using Lycia.Saga.Handlers;
using Sample.Shared.Messages.Events;
using Sample.Shared.Messages.Responses;
using Sample.Shared.SagaStates;

namespace Sample.Order.Orchestration.Seq.Consumer.Sagas;

public class ShippingSagaHandler : 
    CoordinatedSagaHandler<PaymentProcessedEvent, CreateOrderSagaData>
{
    public override async Task HandleAsync(PaymentProcessedEvent message)
    {
        // Simulate shipping step
        var shipped = true; // Simulate logic

        if (!shipped)
        {
            // Shipping failed
            await Context.MarkAsFailed<PaymentProcessedEvent>();
            return;
        }

        // Shipping succeeded, complete the saga or trigger next step if needed
        await Context.MarkAsComplete<PaymentProcessedEvent>();
    }

    public override async Task CompensateAsync(PaymentProcessedEvent message)
    {
        // Compensation logic: recall shipment, notify customer, etc.
        Context.Data.ShippingReversed = true;
        await Context.CompensateAndBubbleUp<PaymentProcessedEvent>();
    }
}