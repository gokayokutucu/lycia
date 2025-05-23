using Lycia.Messaging;
using Lycia.Saga.Abstractions;

namespace Lycia.Saga;

public class ReactiveSagaStepFluent<TStep, TMarkedStep>(ISagaContext<TMarkedStep> context, Task operation)
    where TStep : IMessage
    where TMarkedStep : IMessage
{
    public async Task ThenMarkAsComplete()
    {
        await operation;
        await context.MarkAsComplete<TMarkedStep>();
    }

    public async Task ThenMarkAsFailed(FailResponse fail)
    {
        await operation;
        await context.MarkAsFailed<TMarkedStep>();
    }

    public async Task ThenMarkAsCompensated()
    {
        await operation;
        await context.MarkAsCompensated<TMarkedStep>();
    }

    public async Task ThenMarkAsCompensationFailed()
    {
        await operation;
        await context.MarkAsCompensationFailed<TMarkedStep>();
    }
}

public class CoordinatedSagaStepFluent<TStep, TSagaData>(ISagaContext<TStep, TSagaData> context, Task operation)
    where TStep : IMessage
    where TSagaData : SagaData
{
    public async Task ThenMarkAsComplete()
    {
        await operation;
        await context.MarkAsComplete<TStep>();
    }

    public async Task ThenMarkAsFailed(FailResponse fail)
    {
        await operation;
        await context.MarkAsFailed<TStep>();
    }

    public async Task ThenMarkAsCompensated()
    {
        await operation;
        await context.MarkAsCompensated<TStep>();
    }

    public async Task ThenMarkAsCompensationFailed()
    {
        await operation;
        await context.MarkAsCompensationFailed<TStep>();
    }
}