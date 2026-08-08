// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using Lycia.Saga.Messaging.Handlers;
using Lycia.Tests.Messages;
using Lycia.Tests.SagaStates;

namespace Lycia.Tests.Sagas;

/// <summary>Increments a shared counter each time it runs, so tests can assert how many times the dispatcher actually invoked it.</summary>
public class InboxCountingSagaHandler(InboxCountingSagaHandler.InvocationCounter counter)
    : CoordinatedSagaHandler<OrderCreatedEvent, SampleSagaData>
{
    public class InvocationCounter
    {
        public int Count;
    }

    public override Task HandleAsync(OrderCreatedEvent message, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref counter.Count);
        return Task.CompletedTask;
    }
}
