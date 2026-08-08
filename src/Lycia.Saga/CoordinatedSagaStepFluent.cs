// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using Lycia.Common.Messaging;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Contexts;
using Lycia.Saga.Abstractions.Messaging;

namespace Lycia.Saga;

// TInitialMessage is the type of message that the ISagaContext is primarily associated with.

public class CoordinatedSagaStepFluent<TInitialMessage, TSagaData>(
    ISagaContext<TInitialMessage, TSagaData> context,
    Func<CancellationToken, Task> operation,
    CancellationToken capturedCancellationToken = default) : ISagaStepFluent
    where TInitialMessage : IMessage
    where TSagaData : SagaData
{
    public static object Create(Type stepType, Type sagaDataType, object context, Func<CancellationToken, Task> operation,
        CancellationToken capturedCancellationToken = default)
    {
        var open = typeof(CoordinatedSagaStepFluent<,>);
        var closed = open.MakeGenericType(stepType, sagaDataType);
        return Activator.CreateInstance(closed, context, operation, capturedCancellationToken)!;
    }

    // Preferred: pass the token here, not to the WithTracking(...) call that created this instance.
    // If the caller still supplied one there, it is used as a fallback when this token is left default,
    // for source compatibility with the WithTracking(msg, cancellationToken).Then...() call shape.
    private async Task RunAsync(CancellationToken cancellationToken, Func<CancellationToken, Task> transition)
    {
        var token = SagaStepFluentToken.Resolve(cancellationToken, capturedCancellationToken);
        token.ThrowIfCancellationRequested();
        await operation(token);
        await transition(token);
    }

    /// <summary>Transitions the step the context was constructed for (the step being handled, not the outgoing message).</summary>
    public Task ThenMarkAsComplete(CancellationToken cancellationToken = default) =>
        RunAsync(cancellationToken, token => context.MarkAsComplete<TInitialMessage>(token));

    /// <summary>Explicit form of <see cref="ThenMarkAsComplete(CancellationToken)"/> naming the step at the call site.</summary>
    public Task ThenMarkAsComplete<TStep>(CancellationToken cancellationToken = default) where TStep : IMessage =>
        RunAsync(cancellationToken, token => context.MarkAsComplete<TStep>(token));

    public Task ThenMarkAsFailed(FailResponse fail, CancellationToken cancellationToken = default) =>
        RunAsync(cancellationToken, token => context.MarkAsFailed<TInitialMessage>(fail, token));

    /// <summary>Transitions the step the context was constructed for to failed, without a <see cref="FailResponse"/>.</summary>
    public Task ThenMarkAsFailed(CancellationToken cancellationToken = default) =>
        RunAsync(cancellationToken, token => context.MarkAsFailed<TInitialMessage>(token));

    /// <summary>Explicit form of <see cref="ThenMarkAsFailed(CancellationToken)"/> naming the step at the call site.</summary>
    public Task ThenMarkAsFailed<TStep>(CancellationToken cancellationToken = default) where TStep : IMessage =>
        RunAsync(cancellationToken, token => context.MarkAsFailed<TStep>(token));

    /// <summary>Transitions the step the context was constructed for to cancelled.</summary>
    public Task ThenMarkAsCancelled(CancellationToken cancellationToken = default) =>
        RunAsync(cancellationToken, token => context.MarkAsCancelled<TInitialMessage>(cancellationToken: token));

    /// <summary>Explicit form of <see cref="ThenMarkAsCancelled(CancellationToken)"/> naming the step at the call site.</summary>
    public Task ThenMarkAsCancelled<TStep>(CancellationToken cancellationToken = default) where TStep : IMessage =>
        RunAsync(cancellationToken, token => context.MarkAsCancelled<TStep>(cancellationToken: token));

    /// <summary>Transitions and bubbles up compensation for the step the context was constructed for.</summary>
    public Task ThenMarkAsCompensated(CancellationToken cancellationToken = default) =>
        RunAsync(cancellationToken, token => context.CompensateAndBubbleUp<TInitialMessage>(token));

    /// <summary>Explicit form of <see cref="ThenMarkAsCompensated(CancellationToken)"/> naming the step at the call site.</summary>
    public Task ThenMarkAsCompensated<TStep>(CancellationToken cancellationToken = default) where TStep : IMessage =>
        RunAsync(cancellationToken, token => context.CompensateAndBubbleUp<TStep>(token));

    public Task ThenMarkAsCompensationFailed(CancellationToken cancellationToken = default) =>
        RunAsync(cancellationToken, token => context.MarkAsCompensationFailed<TInitialMessage>(token));

    /// <summary>Explicit form of <see cref="ThenMarkAsCompensationFailed(CancellationToken)"/> naming the step at the call site.</summary>
    public Task ThenMarkAsCompensationFailed<TStep>(CancellationToken cancellationToken = default) where TStep : IMessage =>
        RunAsync(cancellationToken, token => context.MarkAsCompensationFailed<TStep>(token));
}
