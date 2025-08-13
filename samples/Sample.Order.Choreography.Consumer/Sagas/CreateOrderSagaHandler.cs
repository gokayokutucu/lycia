using Lycia.Saga.Handlers;
using Lycia.Saga.Handlers.Abstractions;
using Sample.Shared.Messages.Commands;
using Sample.Shared.Messages.Events;

namespace Sample.Order.Choreography.Consumer.Sagas;

public class CreateOrderSagaHandler :
    StartReactiveSagaHandler<CreateOrderCommand>,
    ISagaCompensationHandler<PaymentFailedEvent>,
    ISagaCompensationHandler<OrderShippingFailedEvent>
{
    public override async Task HandleStartAsync(CreateOrderCommand cmd, CancellationToken cancellationToken = default)
    {
        // Check if already completed to avoid duplicate processing for idempotency
        // This is important in a reactive saga where the same command might be retried
        if (await Context.IsAlreadyCompleted<CreateOrderCommand>(cancellationToken)) return;
        
        await Context.Publish(new OrderCreatedEvent
        {
            OrderId = cmd.OrderId,
            ParentMessageId = cmd.MessageId
        }, cancellationToken);
        await Context.MarkAsComplete<CreateOrderCommand>(cancellationToken);
    }

    // Optional – compensate on payment failure (reactive, not orchestration)
    public async Task CompensateAsync(PaymentFailedEvent failed, CancellationToken cancellationToken = default)
    {
        if (await Context.IsAlreadyCompleted<CreateOrderCommand>(cancellationToken)) return;
        // e.g., notify user / mark order canceled / audit
    }

    public Task CompensateAsync(OrderShippingFailedEvent failed, CancellationToken cancellationToken = default)
    {
        // e.g., refund or notify depending on your business
        return Task.CompletedTask;
    }
}