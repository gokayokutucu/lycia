// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Saga.Handlers;
using Lycia.Tests.Messages;
using Lycia.Tests.SagaStates;

namespace Lycia.Tests.Sagas;

public class SwallowingSagaHandler : CoordinatedSagaHandler<OrderCreatedEvent, SampleSagaData>
{
    public override Task HandleAsync(OrderCreatedEvent message, CancellationToken cancellationToken = default)
    {
        try
        {
            throw new InvalidOperationException("Swallowed exception");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception swallowed: {ex.Message}");
        }

        return Task.CompletedTask;
    }
}