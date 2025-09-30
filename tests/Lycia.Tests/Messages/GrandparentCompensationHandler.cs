// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0


using Lycia.Saga.Abstractions.Handlers;

namespace Lycia.Tests.Messages;

public class GrandparentCompensationHandler : ISagaCompensationHandler<DummyEvent>
{
    public static readonly List<string> Invocations = [];

    public Task CompensateAsync(DummyEvent message, CancellationToken cancellationToken = default)
    {
        Invocations.Add(nameof(GrandparentCompensationHandler));
        return Task.CompletedTask;
    }
}

public class ParentCompensationHandler : ISagaCompensationHandler<DummyEvent>
{
    public static readonly List<string> Invocations = [];

    public Task CompensateAsync(DummyEvent message, CancellationToken cancellationToken = default)
    {
        Invocations.Add(nameof(ParentCompensationHandler));
        return Task.CompletedTask;
    }
}

public class ChildCompensationHandler : ISagaCompensationHandler<DummyEvent>
{
    public static readonly List<string> Invocations = [];

    public Task CompensateAsync(DummyEvent message, CancellationToken cancellationToken = default)
    {
        Invocations.Add(nameof(ChildCompensationHandler));
        return Task.CompletedTask;
    }
}