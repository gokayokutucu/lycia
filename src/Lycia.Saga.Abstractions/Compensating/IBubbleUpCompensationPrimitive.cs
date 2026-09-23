// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Saga.Abstractions.Messaging;

namespace Lycia.Saga.Abstractions.Compensating;

/// <summary>
/// The execution primitive behind <c>MarkAsCompensated&lt;TStep&gt;().ThenBubbleUp(ct)</c>: marks the
/// current step compensated and durably requires - then immediately attempts - propagation to the logical
/// parent. Deliberately <c>internal</c>, not a member of <see cref="Contexts.ISagaContext{TInitialMessage}"/>:
/// application code must reach it only through the staged fluent grammar
/// (<c>MarkAsCompensated&lt;TStep&gt;()...ThenBubbleUp(ct)</c>), never by calling it directly on the
/// context. Every saga context implementation implements both this interface and the no-token
/// <c>MarkAsCompensated&lt;TStep&gt;()</c> overload that constructs the <see cref="SagaCompensatedContinuation"/>
/// closing over it by casting <c>this</c>.
/// </summary>
internal interface IBubbleUpCompensationPrimitive
{
    /// <summary>
    /// Marks the current saga step compensated and durably requires - then immediately attempts -
    /// compensation propagation to the logical parent (via <c>ParentMessageId</c>). A step with no logical
    /// parent (a root step) only marks itself compensated; no propagation requirement is created. Once the
    /// propagation requirement is durably recorded, a crash or cancellation during the immediate attempt
    /// never erases it - a <c>CompensationWorker</c> recovers it. See <c>DEVELOPERS.md</c>, "Coordinated
    /// compensation continuation", for the durable propagation model.
    /// </summary>
    Task BubbleUpCompensationAsync<TStep>(CancellationToken cancellationToken = default) where TStep : IMessage;
}
