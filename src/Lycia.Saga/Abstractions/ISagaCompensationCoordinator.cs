// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Messaging;

namespace Lycia.Saga.Abstractions;

public interface ISagaCompensationCoordinator
{
    Task CompensateAsync(Guid sagaId, Type failedStepType, Type? handlerType, IMessage message, SagaStepFailureInfo? failInfo, CancellationToken cancellationToken = default);
    Task CompensateParentAsync(Guid sagaId, Type stepType, Type handlerType, IMessage message, CancellationToken cancellationToken = default);
}