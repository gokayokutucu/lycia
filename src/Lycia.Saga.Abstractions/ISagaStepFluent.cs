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
public interface ISagaStepFluent
{
    Task ThenMarkAsComplete(CancellationToken cancellationToken = default);
    Task ThenMarkAsFailed(FailResponse fail, CancellationToken cancellationToken = default);
    Task ThenMarkAsCompensated(CancellationToken cancellationToken = default);
    Task ThenMarkAsCompensationFailed(CancellationToken cancellationToken = default);
}