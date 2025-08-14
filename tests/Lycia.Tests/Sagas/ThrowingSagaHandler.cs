using Lycia.Saga.Handlers;
using Lycia.Tests.Messages;
using Lycia.Tests.SagaStates;

namespace Lycia.Tests.Sagas;

public class ThrowingSagaHandler : CoordinatedSagaHandler<OrderCreatedEvent, SampleSagaData>
{
    public override async Task HandleAsync(OrderCreatedEvent message, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("Intentional test exception");
    }
}