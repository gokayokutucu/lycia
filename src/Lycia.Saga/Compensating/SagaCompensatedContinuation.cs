// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using Lycia.Saga.Abstractions.Compensating;

namespace Lycia.Saga.Compensating;

/// <summary>
/// Default <see cref="ICompensatedContinuation"/> returned by the no-token
/// <c>Context.MarkAsCompensated&lt;TStep&gt;()</c> overload. Holds a deferred delegate that closes over the
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
