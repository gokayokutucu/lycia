// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

namespace Lycia.Saga.Abstractions.Compensating;

/// <summary>
/// The staged fluent continuation returned by the no-token
/// <c>Context.MarkAsCompensated&lt;TStep&gt;()</c> overload. Its only member,
/// <see cref="ThenBubbleUp"/>, is the terminal method of the two-stage
/// <c>MarkAsCompensated&lt;TStep&gt;().ThenBubbleUp(cancellationToken)</c> chain.
/// </summary>
public interface ICompensatedContinuation
{
    /// <summary>
    /// Terminal: continues compensation through the logical parent lineage (via <c>ParentMessageId</c>),
    /// invoking the parent's compensation handler, for the step named by the preceding
    /// <c>MarkAsCompensated&lt;TStep&gt;()</c> call. This is the only application-reachable entry point to
    /// that propagation - there is no equivalent method directly on <c>ISagaContext</c>.
    /// <paramref name="cancellationToken"/> is the single token governing the whole two-stage operation,
    /// from the initial mark through parent propagation.
    /// </summary>
    Task ThenBubbleUp(CancellationToken cancellationToken);
}
