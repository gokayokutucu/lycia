// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using Lycia.Common.Messaging;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Contexts;
using Lycia.Saga.Abstractions.Messaging;

namespace Lycia.Saga;

public class ReactiveSagaStepFluent<TInitialMessage>(ISagaContext<TInitialMessage> context, Func<Task> operation) : ISagaStepFluent
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

    public async Task ThenMarkAsFailed(FailResponse fail, CancellationToken cancellationToken = default)
    {
        await operation();
        await context.MarkAsFailed<TInitialMessage>(cancellationToken);
    }

    public async Task ThenMarkAsCompensated(CancellationToken cancellationToken = default)
    {
        await operation();
        await context.MarkAsCompensated<TInitialMessage>();
    }

    public async Task ThenMarkAsCompensationFailed()
    {
        await operation();
        await context.MarkAsCompensationFailed<TInitialMessage>();
    }
}