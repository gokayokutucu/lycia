// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Handlers;
using Lycia.Tests.Messages;
using Lycia.Tests.SagaStates;

namespace Lycia.Tests.Sagas;

public class TestStartReactiveCompensateHandler : StartCoordinatedSagaHandler<CreateOrderCommand, SampleSagaData>
{
    public static bool CompensateCalled = false;
    public override Task HandleStartAsync(CreateOrderCommand message, CancellationToken cancellationToken = default)
    {
        // Mark step as failed so that compensation triggers
        return Context.MarkAsFailed<CreateOrderCommand>(cancellationToken);
    }
    public override Task CompensateStartAsync(CreateOrderCommand message, CancellationToken cancellationToken = default)
    {
        CompensateCalled = true;
        return Task.CompletedTask;
    }
}