using Lycia.Saga.Handlers;
using Sample.Shared.Messages.Events;

namespace Sample.Order.Consumer.Sagas;

public class AuditOrderSagaHandler : ReactiveSagaHandler<OrderCreatedEvent>
{
    public override async Task HandleAsync(OrderCreatedEvent message)
    {
        try
        {
            await Context.MarkAsComplete<OrderCreatedEvent>();
        }
        catch (Exception e)
        {
            Console.WriteLine($"🚨 Audit failed: {e.Message}");
            await Context.MarkAsFailed<OrderCreatedEvent>();
        }
    }
}