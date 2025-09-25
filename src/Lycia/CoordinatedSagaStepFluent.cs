// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using Lycia.Abstractions;
using Lycia.Messaging;

namespace Lycia;

// TInitialMessage is the type of message that the ISagaContext is primarily associated with.

public class CoordinatedSagaStepFluent<TInitialMessage, TSagaData>(
    ISagaContext<TInitialMessage, TSagaData> context,
    Func<Task> operation) : ISagaStepFluent
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

    public async Task ThenMarkAsFailed(FailResponse fail, CancellationToken cancellationToken = default)
    {
        await operation();
        await context.MarkAsFailed<TInitialMessage>(cancellationToken);
    }

    public async Task ThenMarkAsCompensated(CancellationToken cancellationToken = default)
    {
        await operation();
        await context.CompensateAndBubbleUp<TInitialMessage>(cancellationToken);
    }

    public async Task ThenMarkAsCompensationFailed()
    {
        await operation();
        await context.MarkAsCompensationFailed<TInitialMessage>();
    }
}