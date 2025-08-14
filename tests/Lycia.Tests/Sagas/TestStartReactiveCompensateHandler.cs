using Lycia.Saga.Handlers;
using Lycia.Tests.Messages;
using Lycia.Tests.SagaStates;

namespace Lycia.Tests.Sagas;

public class TestStartReactiveCompensateHandler : StartCoordinatedSagaHandler<CreateOrderCommand, SampleSagaData>
{
    public static bool CompensateCalled = false;
    public override Task HandleStartAsync(CreateOrderCommand message)
    {
        // Mark step as failed so that compensation triggers
        return Context.MarkAsFailed<CreateOrderCommand>();
    }
    public override Task CompensateStartAsync(CreateOrderCommand message)
    {
        CompensateCalled = true;
        return Task.CompletedTask;
    }
}