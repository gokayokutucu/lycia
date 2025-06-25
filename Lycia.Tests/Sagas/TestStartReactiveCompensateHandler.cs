using Lycia.Saga.Handlers;
using Sample.Shared.Messages.Commands;

namespace Lycia.Tests.Sagas;

public class TestStartReactiveCompensateHandler : StartReactiveSagaHandler<CreateOrderCommand>
{
    public static bool CompensateCalled = false;
    public override Task HandleStartAsync(CreateOrderCommand message)
    {
        // Mark step as failed so that compensation triggers
        return MarkAsFailed();
    }
    public override Task CompensateStartAsync(CreateOrderCommand message)
    {
        CompensateCalled = true;
        return Task.CompletedTask;
    }
}