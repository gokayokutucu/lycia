// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using Lycia.Saga.Messaging.Handlers;
using Lycia.Tests.Helpers;

namespace Lycia.Tests.Messages;

public class GrandparentCompensationSagaHandler : StartCoordinatedSagaHandler<DummyGrandparentEvent, DummySagaData>
{
    public static readonly List<string> Invocations = [];

    public override Task HandleStartAsync(DummyGrandparentEvent message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public override Task CompensateStartAsync(DummyGrandparentEvent message, CancellationToken cancellationToken = default)
    {
        Invocations.Add(nameof(GrandparentCompensationSagaHandler));
        Context.CompensateAndBubbleUp<DummyGrandparentEvent>(cancellationToken);
        return Task.CompletedTask;
    }
}

public class ParentCompensationSagaHandler : CoordinatedSagaHandler<DummyParentEvent, DummySagaData>
{
    public static readonly List<string> Invocations = [];

    public override Task HandleAsync(DummyParentEvent message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public override Task CompensateAsync(DummyParentEvent message, CancellationToken cancellationToken = default)
    {
        Invocations.Add(nameof(ParentCompensationSagaHandler));
        Context.CompensateAndBubbleUp<DummyParentEvent>(cancellationToken);
        return Task.CompletedTask;
    }
}

public class ChildCompensationSagaHandler : CoordinatedSagaHandler<DummyChildEvent, DummySagaData>
{
    public static readonly List<string> Invocations = [];

    public override Task HandleAsync(DummyChildEvent message, CancellationToken cancellationToken = default)
    {
        if (message.IsFailed)
        {
            Context.MarkAsFailed<DummyChildEvent>(cancellationToken);
        }
        return Task.CompletedTask;
    }

    public override Task CompensateAsync(DummyChildEvent message, CancellationToken cancellationToken = default)
    {
        Invocations.Add(nameof(ChildCompensationSagaHandler));
        if (message.IsCompensationFailed)
            Context.MarkAsCompensationFailed<DummyChildEvent>();
        else
            Context.CompensateAndBubbleUp<DummyChildEvent>(cancellationToken);
        return Task.CompletedTask;
    }
}