// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using Lycia.Common.SagaSteps;
using Lycia.Saga.Abstractions.Messaging;

namespace Lycia.Saga.Abstractions;

public interface ISagaCompensationCoordinator
{
    Task CompensateAsync(Guid sagaId, Type failedStepType, Type? handlerType, IMessage message, SagaStepFailureInfo? failInfo, CancellationToken cancellationToken = default);
    Task CompensateParentAsync(Guid sagaId, Type stepType, Type handlerType, IMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Advanced imperative primitive behind <c>Context.BubbleUpCompensation(failedEvent, cancellationToken)</c>.
    /// Requires the current step to already be durably <c>Compensated</c> (via a prior <c>MarkAsCompensated</c>
    /// call) and requires <paramref name="failedEvent"/> to identify <paramref name="currentStep"/> exactly;
    /// see the implementation's remarks for the full precondition and idempotency contract.
    /// </summary>
    Task BubbleUpCompensationAsync(Guid sagaId, Type stepType, Type handlerType, IMessage currentStep,
        IMessage failedEvent, CancellationToken cancellationToken = default);
}