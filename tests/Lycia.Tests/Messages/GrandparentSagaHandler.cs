using Lycia.Saga.Handlers;

namespace Lycia.Tests.Messages;

public class GrandparentCompensationSagaHandler : StartReactiveSagaHandler<DummyGrandparentEvent>
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

public class ParentCompensationSagaHandler : ReactiveSagaHandler<DummyParentEvent>
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

public class ChildCompensationSagaHandler : ReactiveSagaHandler<DummyChildEvent>
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
            Context.MarkAsCompensationFailed<DummyChildEvent>(cancellationToken);
        else
            Context.CompensateAndBubbleUp<DummyChildEvent>(cancellationToken);
        return Task.CompletedTask;
    }
}