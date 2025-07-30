using Lycia.Saga.Handlers;
using Sample.Shared.Messages.Events;

namespace Sample.Order.Consumer.Sagas;

public class AuditOrderSagaHandler : ReactiveSagaHandler<OrderCreatedEvent>
{
    public override async Task HandleAsync(OrderCreatedEvent message)
    {
        try
        {
            await Context.MarkAsFailed<OrderCreatedEvent>();
        }
        catch (Exception e)
        {
            Console.WriteLine($"🚨 Audit failed: {e.Message}");
            await Context.MarkAsFailed<OrderCreatedEvent>();
        }
    }

    public override async Task CompensateAsync(OrderCreatedEvent message)
    {
        try
        {
            await Context.MarkAsCompensated<OrderCreatedEvent>();
        }
        catch (Exception e)
        {
            await Context.MarkAsCompensationFailed<OrderCreatedEvent>();
            throw;
        }
    }
}