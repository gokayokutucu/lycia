using Lycia.Dapr.Messages;

namespace Lycia.Dapr;

public class OrderCreatedEventHandler : MyEventHandler<OrderCreated>
{
    public override Task Handle(OrderCreated @event)
    {
        Console.WriteLine(@event.OrderId);

        return Task.CompletedTask;
    }
}