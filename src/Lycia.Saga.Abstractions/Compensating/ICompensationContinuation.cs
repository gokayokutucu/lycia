// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using Lycia.Saga.Abstractions.Messaging;

namespace Lycia.Saga.Abstractions.Compensating;

/// <summary>
/// The staged fluent continuation returned by <c>Context.ContinueCompensation()</c>. It performs no
/// business rollback and executes nothing by itself; it only exposes the two valid next steps. Nothing
/// beyond this interface's own scope is deferred: the framework-level compensation state transition it
/// wraps is <see cref="Lycia.Saga.Abstractions.Contexts.ISagaContext{TInitialMessage}.MarkAsCompensated{TStep}"/>,
/// the same one the standalone <c>Context.MarkAsCompensated&lt;TStep&gt;(cancellationToken)</c> call makes.
/// </summary>
public interface ICompensationContinuation
{
    /// <summary>
    /// Terminal, two-stage form: marks <typeparamref name="TStep"/> compensated and stops there - it does
    /// not propagate to the logical parent. Equivalent to
    /// <c>Context.MarkAsCompensated&lt;TStep&gt;(cancellationToken)</c>; use this form (or that direct
    /// call) for a root/final compensation step. <paramref name="cancellationToken"/> is the single token
    /// governing the whole operation.
    /// </summary>
    Task ThenMarkAsCompensated<TStep>(CancellationToken cancellationToken) where TStep : IMessage;

    /// <summary>
    /// Non-terminal form: marks <typeparamref name="TStep"/> compensated as the first stage of a
    /// three-stage chain, and returns a continuation that only exposes <c>ThenBubbleUp</c>. Nothing runs
    /// until <c>ThenBubbleUp(cancellationToken)</c> is awaited on the result - that call, not this one,
    /// owns the <see cref="CancellationToken"/> for the whole operation.
    /// </summary>
    ICompensatedContinuation ThenMarkAsCompensated<TStep>() where TStep : IMessage;
}

/// <summary>
/// The staged fluent continuation returned by the no-token
/// <see cref="ICompensationContinuation.ThenMarkAsCompensated{TStep}()"/> overload. Its only member,
/// <see cref="ThenBubbleUp"/>, is the terminal method of the three-stage
/// <c>ContinueCompensation().ThenMarkAsCompensated&lt;TStep&gt;().ThenBubbleUp(cancellationToken)</c> chain.
/// </summary>
public interface ICompensatedContinuation
{
    /// <summary>
    /// Terminal: continues compensation through the logical parent lineage (via <c>ParentMessageId</c>),
    /// invoking the parent's compensation handler. Equivalent to
    /// <c>Context.BubbleUpCompensationAsync&lt;TStep&gt;(cancellationToken)</c> for the step named by the
    /// preceding <c>ThenMarkAsCompensated&lt;TStep&gt;()</c> call. <paramref name="cancellationToken"/> is
    /// the single token governing the whole three-stage operation, from the initial mark through parent
    /// propagation.
    /// </summary>
    Task ThenBubbleUp(CancellationToken cancellationToken);
}
