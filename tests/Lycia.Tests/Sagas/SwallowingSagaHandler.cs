using Lycia.Saga.Handlers;
using Lycia.Tests.Messages;
using Lycia.Tests.SagaStates;

namespace Lycia.Tests.Sagas;

public class SwallowingSagaHandler : CoordinatedSagaHandler<OrderCreatedEvent, SampleSagaData>
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