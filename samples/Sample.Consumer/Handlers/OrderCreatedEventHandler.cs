using Sample.Domain.Messages;

namespace Sample.Consumer.Handlers;

public class OrderCreatedEventHandler : Lycia.Dapr.Messages.EventHandler<OrderCreated>
{
    private int i = 1;

    public override Task Handle(OrderCreated @event)
    {
        Console.WriteLine(@event.OrderId);

        Console.WriteLine("i: " + i.ToString());

        i += 1;

        return Task.CompletedTask;
    }
}