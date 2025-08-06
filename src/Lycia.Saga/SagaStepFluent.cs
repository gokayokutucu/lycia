using Lycia.Messaging;
using Lycia.Saga.Abstractions;

namespace Lycia.Saga;

// TInitialMessage is the type of message that the ISagaContext is primarily associated with.
public class ReactiveSagaStepFluent<TInitialMessage>(
    ISagaContext<TInitialMessage> context,
    Func<Task> operation)
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

public class CoordinatedSagaStepFluent<TInitialMessage, TSagaData>(
    ISagaContext<TInitialMessage, TSagaData> context,
    Func<Task> operation)
    where TInitialMessage : IMessage
    where TSagaData : SagaData
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