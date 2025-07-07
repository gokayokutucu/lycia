using Lycia.Saga.Handlers;
using Sample.Shared.Messages.Events;

namespace Sample.Shared.Messages.Sagas;

public class AuditOrderSagaHandler : ReactiveSagaHandler<OrderCreatedEvent>
{
    public override Task HandleAsync(OrderCreatedEvent message)
    {
        try
        {
            return Context.MarkAsComplete<OrderCreatedEvent>();
        }
        catch (Exception e)
        {
            Console.WriteLine($"🚨 Audit failed: {e.Message}");
            return Context.MarkAsFailed<OrderCreatedEvent>();
        }
    }
}