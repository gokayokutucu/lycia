using Lycia.Saga.Handlers;
using Sample.Shared.Messages.Events;

namespace Sample.Shared.Messages.Sagas;

public class SwallowingSagaHandler : ReactiveSagaHandler<OrderCreatedEvent>
{
    public override Task HandleAsync(OrderCreatedEvent message)
    {
        try
        {
            throw new InvalidOperationException("Swallowed exception");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception swallowed: {ex.Message}");
        }

        return Task.CompletedTask;
    }
}