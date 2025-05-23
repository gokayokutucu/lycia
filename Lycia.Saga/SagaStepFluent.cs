using Lycia.Messaging;
using Lycia.Saga.Abstractions;

namespace Lycia.Saga;

// TStep is the type of message being sent/published by this fluent step.
// TInitialMessage is the type of message that the ISagaContext is primarily associated with (from the handler that initiated this step).
public class ReactiveSagaStepFluent<TStep, TInitialMessage>(ISagaContext<TInitialMessage> context, Task operation)
    where TStep : IMessage
    where TInitialMessage : IMessage
{
    public async Task ThenMarkAsComplete()
    {
        await operation;
        // Mark the initial message type (TInitialMessage) associated with the context as complete.
        await context.MarkAsComplete<TInitialMessage>();
    }

    public async Task ThenMarkAsFailed(FailResponse fail) // fail parameter is present, but not used in MarkAsFailed. Retaining for signature consistency if needed elsewhere.
    {
        await operation;
        // Mark the initial message type (TInitialMessage) associated with the context as failed.
        await context.MarkAsFailed<TInitialMessage>();
    }

    public async Task ThenMarkAsCompensated()
    {
        await operation;
        // Mark the initial message type (TInitialMessage) associated with the context as compensated.
        await context.MarkAsCompensated<TInitialMessage>();
    }

    public async Task ThenMarkAsCompensationFailed()
    {
        await operation;
        // Mark the initial message type (TInitialMessage) associated with the context as compensation failed.
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