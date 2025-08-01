using Lycia.Messaging;
using Lycia.Saga.Abstractions;

namespace Lycia.Saga;

// TInitialMessage is the type of message that the ISagaContext is primarily associated with.
public class ReactiveSagaStepFluent<TStep, TInitialMessage>(
    ISagaContext<TInitialMessage> context,
    Func<Task> operation,
    TInitialMessage currentStep,
    TStep initialMessage)
    where TStep : IMessage
    where TInitialMessage : IMessage
{
    
    public async Task ThenMarkAsComplete()
    {
        await operation();
        await context.MarkAsComplete<TInitialMessage>();
    }

    public async Task ThenMarkAsFailed(FailResponse fail)
    {
        await operation();
        await context.MarkAsFailed<TInitialMessage>();
    }

    public async Task ThenMarkAsCompensated()
    {
        await operation();
        await context.CompensateAndBubbleUp<TInitialMessage>();
    }

    public async Task ThenMarkAsCompensationFailed()
    {
        await operation();
        await context.MarkAsCompensationFailed<TInitialMessage>();
    }
}

public class CoordinatedSagaStepFluent<TStep, TSagaData>(
    ISagaContext<TStep, TSagaData> context,
    Func<Task> operation,
    TStep @event)
    where TStep : IMessage
    where TSagaData : new()
{
    public async Task ThenMarkAsComplete()
    {
        await operation();
        await context.MarkAsComplete<TStep>();
    }

    public async Task ThenMarkAsFailed(FailResponse fail)
    {
        await operation();
        await context.MarkAsFailed<TStep>();
    }

    public async Task ThenMarkAsCompensated()
    {
        await operation();
        await context.CompensateAndBubbleUp<TStep>();
    }

    public async Task ThenMarkAsCompensationFailed()
    {
        await operation();
        await context.MarkAsCompensationFailed<TStep>();
    }
}