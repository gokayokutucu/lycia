// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using Lycia.Common.Messaging;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Contexts;
using Lycia.Saga.Abstractions.Messaging;

namespace Lycia.Saga;

public class ReactiveSagaStepFluent<TInitialMessage>(
    ISagaContext<TInitialMessage> context,
    Func<CancellationToken, Task> operation,
    CancellationToken capturedCancellationToken = default) : ISagaStepFluent
    where TInitialMessage : IMessage
{
    public static object Create(Type stepType, object context, Func<CancellationToken, Task> operation,
        CancellationToken capturedCancellationToken = default)
    {
        var open = typeof(ReactiveSagaStepFluent<>);
        var closed = open.MakeGenericType(stepType);
        return Activator.CreateInstance(closed, context, operation, capturedCancellationToken)!;
    }

    // Preferred: pass the token here, not to the WithTracking(...) call that created this instance.
    // If the caller still supplied one there, it is used as a fallback when this token is left default,
    // for source compatibility with the WithTracking(msg, cancellationToken).Then...() call shape.
    public async Task ThenMarkAsComplete(CancellationToken cancellationToken = default)
    {
        var token = SagaStepFluentToken.Resolve(cancellationToken, capturedCancellationToken);
        token.ThrowIfCancellationRequested();
        await operation(token);
        await context.MarkAsComplete<TInitialMessage>(token);
    }

    public async Task ThenMarkAsFailed(FailResponse fail, CancellationToken cancellationToken = default)
    {
        var token = SagaStepFluentToken.Resolve(cancellationToken, capturedCancellationToken);
        token.ThrowIfCancellationRequested();
        await operation(token);
        await context.MarkAsFailed<TInitialMessage>(token);
    }

    public async Task ThenMarkAsCompensated(CancellationToken cancellationToken = default)
    {
        var token = SagaStepFluentToken.Resolve(cancellationToken, capturedCancellationToken);
        token.ThrowIfCancellationRequested();
        await operation(token);
        await context.MarkAsCompensated<TInitialMessage>(token);
    }

    public async Task ThenMarkAsCompensationFailed(CancellationToken cancellationToken = default)
    {
        var token = SagaStepFluentToken.Resolve(cancellationToken, capturedCancellationToken);
        token.ThrowIfCancellationRequested();
        await operation(token);
        await context.MarkAsCompensationFailed<TInitialMessage>(token);
    }
}
