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
        // Root step: no logical parent, so this only marks itself compensated.
        return Context.MarkAsCompensated<DummyGrandparentEvent>(cancellationToken);
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
        return Context
            .MarkAsCompensated<DummyParentEvent>()
            .ThenBubbleUp(cancellationToken);
    }
}

public class ChildCompensationSagaHandler : CoordinatedSagaHandler<DummyChildEvent, DummySagaData>
{
    public static readonly List<string> Invocations = [];

    public override Task HandleAsync(DummyChildEvent message, CancellationToken cancellationToken = default)
    {
        if (message.IsFailed)
        {
            return Context.MarkAsFailed<DummyChildEvent>(cancellationToken);
        }
        return Task.CompletedTask;
    }

    public override Task CompensateAsync(DummyChildEvent message, CancellationToken cancellationToken = default)
    {
        Invocations.Add(nameof(ChildCompensationSagaHandler));
        if (message.IsCompensationFailed)
            return Context.MarkAsCompensationFailed<DummyChildEvent>(cancellationToken);

        return Context
            .MarkAsCompensated<DummyChildEvent>()
            .ThenBubbleUp(cancellationToken);
    }
}
