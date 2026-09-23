// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using Lycia.Saga.Abstractions.Compensating;
using Lycia.Saga.Abstractions.Contexts;
using Lycia.Saga.Abstractions.Messaging;

namespace Lycia.Saga.Compensating;

/// <summary>
/// Default <see cref="ICompensationContinuation"/> returned by <c>Context.ContinueCompensation()</c>. Holds
/// only a reference to the saga context; it performs no work until one of its terminal methods is awaited,
/// and it never captures a <see cref="CancellationToken"/> of its own.
/// </summary>
internal sealed class SagaCompensationContinuation<TInitialMessage>(ISagaContext<TInitialMessage> context)
    : ICompensationContinuation
    where TInitialMessage : IMessage
{
    /// <inheritdoc />
    public Task ThenMarkAsCompensated<TStep>(CancellationToken cancellationToken) where TStep : IMessage
    {
        cancellationToken.ThrowIfCancellationRequested();
        return context.MarkAsCompensated<TStep>(cancellationToken);
    }

    /// <inheritdoc />
    public ICompensatedContinuation ThenMarkAsCompensated<TStep>() where TStep : IMessage
    {
        // IBubbleUpCompensationPrimitive is deliberately internal (see its doc comment) so application
        // code cannot call BubbleUpCompensationAsync directly on the context - only through this staged
        // chain. Every built-in ISagaContext implementation implements it; a custom implementation that
        // doesn't cannot support ThenBubbleUp, which this check reports clearly instead of an opaque
        // InvalidCastException.
        if (context is not IBubbleUpCompensationPrimitive primitive)
            throw new InvalidOperationException(
                $"'{context.GetType().FullName}' does not support compensation bubble-up " +
                $"(it does not implement the internal IBubbleUpCompensationPrimitive). " +
                "ThenBubbleUp is only available for ISagaContext implementations provided by Lycia.");

        return new SagaCompensatedContinuation(primitive.BubbleUpCompensationAsync<TStep>);
    }
}

/// <summary>
/// Default <see cref="ICompensatedContinuation"/> returned by the no-token
/// <c>ThenMarkAsCompensated&lt;TStep&gt;()</c> overload. Holds a deferred delegate that closes over the
/// step type named at that call; nothing runs until <see cref="ThenBubbleUp"/> is awaited.
/// </summary>
internal sealed class SagaCompensatedContinuation(Func<CancellationToken, Task> bubbleUp) : ICompensatedContinuation
{
    /// <inheritdoc />
    public Task ThenBubbleUp(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return bubbleUp(cancellationToken);
    }
}
