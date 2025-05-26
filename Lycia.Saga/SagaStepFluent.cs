using Lycia.Messaging;
using Lycia.Saga.Abstractions;

namespace Lycia.Saga;

// TInitialMessage is the type of message that the ISagaContext is primarily associated with.
public class ReactiveSagaStepFluent<TStep, TInitialMessage>(ISagaContext<TInitialMessage> context, Task operation)
    where TStep : IMessage
    where TInitialMessage : IMessage
{
    public async Task ThenMarkAsComplete()
    {
        await operation;
        await context.MarkAsComplete<TInitialMessage>();
    }

    public async Task ThenMarkAsFailed(FailResponse fail)
    {
        await operation;
        await context.MarkAsFailed<TInitialMessage>();
    }

    public async Task ThenMarkAsCompensated()
    {
        await operation;
        await context.MarkAsCompensated<TInitialMessage>();
    }

    public async Task ThenMarkAsCompensationFailed()
    {
        await operation;
        await context.MarkAsCompensationFailed<TInitialMessage>();
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