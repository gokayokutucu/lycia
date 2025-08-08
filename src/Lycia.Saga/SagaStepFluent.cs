using System.Collections.Concurrent;
using Lycia.Messaging;
using Lycia.Saga.Abstractions;

namespace Lycia.Saga;

// TInitialMessage is the type of message that the ISagaContext is primarily associated with.
public class ReactiveSagaStepFluent<TInitialMessage>(
    ISagaContext<TInitialMessage> context,
    Func<Task> operation)
    where TInitialMessage : IMessage
{
    public static object Create(Type stepType, object context, Func<Task> operation)
    {
        var open = typeof(ReactiveSagaStepFluent<>);
        var closed = open.MakeGenericType(stepType);
        return Activator.CreateInstance(closed, context, operation)!;
    }
    
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

public interface ICoordinatedSagaStepFluent
{
    Task ThenMarkAsComplete();
    Task ThenMarkAsFailed(FailResponse fail);
    Task ThenMarkAsCompensated();
    Task ThenMarkAsCompensationFailed();
}

public class CoordinatedSagaStepFluent<TInitialMessage, TSagaData>(
    ISagaContext<TInitialMessage, TSagaData> context,
    Func<Task> operation) : ICoordinatedSagaStepFluent
    where TInitialMessage : IMessage
    where TSagaData : SagaData
{
    public static object Create(Type stepType, Type sagaDataType, object context, Func<Task> operation)
    {
        var open = typeof(CoordinatedSagaStepFluent<,>);
        var closed = open.MakeGenericType(stepType, sagaDataType);
        return Activator.CreateInstance(closed, context, operation)!;
    }
    
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