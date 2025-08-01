using Lycia.Saga.Abstractions;
using Lycia.Saga.Handlers.Abstractions;

namespace Lycia.Tests.Messages;

public class GrandparentCompensationHandler : ISagaCompensationHandler<DummyEvent>
{
    public static readonly List<string> Invocations = [];

    public Task CompensateAsync(DummyEvent message)
    {
        Invocations.Add(nameof(GrandparentCompensationHandler));
        return Task.CompletedTask;
    }
}

public class ParentCompensationHandler : ISagaCompensationHandler<DummyEvent>
{
    public static readonly List<string> Invocations = [];

    public Task CompensateAsync(DummyEvent message)
    {
        Invocations.Add(nameof(ParentCompensationHandler));
        return Task.CompletedTask;
    }
}

public class ChildCompensationHandler : ISagaCompensationHandler<DummyEvent>
{
    public static readonly List<string> Invocations = [];

    public Task CompensateAsync(DummyEvent message)
    {
        Invocations.Add(nameof(ChildCompensationHandler));
        return Task.CompletedTask;
    }
}