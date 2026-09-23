// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using Lycia.Common.Messaging;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Contexts;
using Lycia.Saga.Abstractions.Messaging;

namespace Lycia.Saga;

/// <summary>
/// Terminal continuation for a deferred, reactive (non-coordinated) tracked message operation created by
/// <c>SendWithTracking</c>/<c>PublishWithTracking</c>/<c>RespondWithTracking</c>/<c>ScheduleWithTracking</c>.
/// The underlying message operation does not run until a terminal <c>Then...</c> method here is awaited;
/// that method's <see cref="CancellationToken"/> is the single token governing the whole deferred
/// operation - both the outgoing message and the saga-step transition that follows it. The WithTracking
/// call that created this instance never accepts a token of its own.
/// </summary>
public class ReactiveSagaStepFluent<TInitialMessage>(
    ISagaContext<TInitialMessage> context,
    Func<CancellationToken, Task> operation) : ISagaStepFluent
    where TInitialMessage : IMessage
{
    public static object Create(Type stepType, object context, Func<CancellationToken, Task> operation)
    {
        var open = typeof(ReactiveSagaStepFluent<>);
        var closed = open.MakeGenericType(stepType);
        return Activator.CreateInstance(closed, context, operation)!;
    }

    private async Task RunAsync(CancellationToken cancellationToken, Func<CancellationToken, Task> transition)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await operation(cancellationToken);
        await transition(cancellationToken);
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

    /// <summary>
    /// Transitions the step the context was constructed for to compensated. This does not bubble
    /// compensation up to the logical parent; use <c>Context.ContinueCompensation()</c> for that.
    /// </summary>
    public Task ThenMarkAsCompensated(CancellationToken cancellationToken = default) =>
        RunAsync(cancellationToken, token => context.MarkAsCompensated<TInitialMessage>(token));

    /// <summary>Explicit form of <see cref="ThenMarkAsCompensated(CancellationToken)"/> naming the step at the call site.</summary>
    public Task ThenMarkAsCompensated<TStep>(CancellationToken cancellationToken = default) where TStep : IMessage =>
        RunAsync(cancellationToken, token => context.MarkAsCompensated<TStep>(token));

    public Task ThenMarkAsCompensationFailed(CancellationToken cancellationToken = default) =>
        RunAsync(cancellationToken, token => context.MarkAsCompensationFailed<TInitialMessage>(token));

    /// <summary>Explicit form of <see cref="ThenMarkAsCompensationFailed(CancellationToken)"/> naming the step at the call site.</summary>
    public Task ThenMarkAsCompensationFailed<TStep>(CancellationToken cancellationToken = default) where TStep : IMessage =>
        RunAsync(cancellationToken, token => context.MarkAsCompensationFailed<TStep>(token));
}
