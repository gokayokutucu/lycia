using Lycia.Saga.Handlers;

namespace Lycia.Tests.Messages;

public class GrandparentCompensationSagaHandler : StartReactiveSagaHandler<DummyGrandparentEvent>
{
    public static readonly List<string> Invocations = [];

    public override Task HandleStartAsync(DummyGrandparentEvent message)
    {
        return Task.CompletedTask;
    }

    public override Task CompensateStartAsync(DummyGrandparentEvent message)
    {
        Invocations.Add(nameof(GrandparentCompensationSagaHandler));
        Context.MarkAsCompensated<DummyGrandparentEvent>();
        return Task.CompletedTask;
    }
}

public class ParentCompensationSagaHandler : ReactiveSagaHandler<DummyParentEvent>
{
    public static readonly List<string> Invocations = [];

    public override Task HandleAsync(DummyParentEvent message)
    {
        return Task.CompletedTask;
    }

    public override Task CompensateAsync(DummyParentEvent message)
    {
        Invocations.Add(nameof(ParentCompensationSagaHandler));
        Context.MarkAsCompensated<DummyParentEvent>();
        return Task.CompletedTask;
    }
}

public class ChildCompensationSagaHandler : ReactiveSagaHandler<DummyChildEvent>
{
    public static readonly List<string> Invocations = [];

    public override Task HandleAsync(DummyChildEvent message)
    {
        return Task.CompletedTask;
    }

    public override Task CompensateAsync(DummyChildEvent message)
    {
        Invocations.Add(nameof(ChildCompensationSagaHandler));
        if (message.IsCompensationFailed)
            Context.MarkAsCompensationFailed<DummyChildEvent>();
        else
            Context.MarkAsCompensated<DummyChildEvent>();
        return Task.CompletedTask;
    }
}