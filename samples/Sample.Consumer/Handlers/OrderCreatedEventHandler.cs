using Sample.Domain.Messages;

namespace Sample.Consumer.Handlers;

public class OrderCreatedEventHandler : Lycia.Dapr.Messages.EventHandler<OrderCreated>
{
    public override Task Handle(OrderCreated @event)
    {
        Console.WriteLine(@event.OrderId);

        return Task.CompletedTask;
    }
}