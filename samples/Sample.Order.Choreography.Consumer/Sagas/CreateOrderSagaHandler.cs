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
    // Use this to enforce idempotency if needed
    // Uncomment if you want to enforce idempotency for this saga
    //protected override bool EnforceIdempotency => true;

    public override async Task HandleStartAsync(CreateOrderCommand cmd, CancellationToken cancellationToken = default)
    {
        await Context.Publish(new OrderCreatedEvent
        {
            OrderId = cmd.OrderId,
            ParentMessageId = cmd.MessageId
        }, cancellationToken);
        await Context.MarkAsComplete<CreateOrderCommand>(cancellationToken);
    }

    public override async Task CompensateStartAsync(CreateOrderCommand message, CancellationToken cancellationToken = default)
    {
        try
        {
            // Compensation logic
            await Context.MarkAsCompensated<CreateOrderCommand>(cancellationToken);
        }
        catch (Exception ex)
        {
            // Log, notify, halt chain, etc.
            Console.WriteLine($"❌ Compensation failed: {ex.Message}");
            
            await Context.MarkAsCompensationFailed<CreateOrderCommand>(cancellationToken);
            // Optionally: rethrow or store for manual retry
            throw; // Or suppress and log for retry system
        }
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