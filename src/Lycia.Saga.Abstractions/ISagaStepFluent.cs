// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using Lycia.Common.Messaging;
using Lycia.Saga;
using Lycia.Saga.Abstractions.Messaging;

namespace Lycia.Saga.Abstractions;

/// <summary>
/// Terminal continuation for a deferred tracked message operation (see <c>SendWithTracking</c>,
/// <c>PublishWithTracking</c>, <c>RespondWithTracking</c>, <c>ScheduleWithTracking</c>). The underlying
/// message operation is not executed until one of these terminal methods is awaited; the
/// <see cref="CancellationToken"/> passed here applies to both the deferred message operation and the
/// saga-step transition that follows it, so a single token governs the whole composite operation.
/// </summary>
/// <remarks>
/// Each transition has two forms:
/// <list type="bullet">
/// <item>
/// A non-generic, current-step-inferred form (for example <see cref="ThenMarkAsComplete"/>) that
/// transitions the step the saga context was constructed for. This is the step being handled when
/// <c>SendWithTracking</c>/<c>PublishWithTracking</c>/<c>RespondWithTracking</c>/<c>ScheduleWithTracking</c>
/// was called — never the outgoing tracked message's type.
/// </item>
/// <item>
/// An explicit generic form (for example <see cref="ThenMarkAsComplete{TStep}"/>) that names the step
/// type at the call site for readability and self-documentation, matching the same
/// <c>ISagaContext.MarkAsComplete&lt;TStep&gt;</c>-style API used by the standalone transitions.
/// </item>
/// </list>
/// </remarks>
public interface ISagaStepFluent
{
    Task ThenMarkAsComplete(CancellationToken cancellationToken = default);
    Task ThenMarkAsComplete<TStep>(CancellationToken cancellationToken = default) where TStep : IMessage;

    Task ThenMarkAsFailed(FailResponse fail, CancellationToken cancellationToken = default);
    Task ThenMarkAsFailed<TStep>(CancellationToken cancellationToken = default) where TStep : IMessage;

    Task ThenMarkAsCancelled(CancellationToken cancellationToken = default);
    Task ThenMarkAsCancelled<TStep>(CancellationToken cancellationToken = default) where TStep : IMessage;

    Task ThenMarkAsCompensated(CancellationToken cancellationToken = default);
    Task ThenMarkAsCompensated<TStep>(CancellationToken cancellationToken = default) where TStep : IMessage;

    Task ThenMarkAsCompensationFailed(CancellationToken cancellationToken = default);
    Task ThenMarkAsCompensationFailed<TStep>(CancellationToken cancellationToken = default) where TStep : IMessage;
}