using Lycia.Saga.Handlers;
using Sample.Shared.Messages.Events;

namespace Sample.Shared.Messages.Sagas;

public class ThrowingSagaHandler : ReactiveSagaHandler<OrderCreatedEvent>
{
    public override async Task HandleAsync(OrderCreatedEvent message)
    {
        throw new InvalidOperationException("Intentional test exception");
    }
}