// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Common.Enums;
using Lycia.Common.SagaSteps;
using Lycia.Compensating;
using Lycia.Extensions.Serialization;
using Lycia.Saga;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Handlers;
using Lycia.Saga.Abstractions.Serializers;
using Lycia.Saga.Contexts;
using Lycia.Stores;
using Lycia.Tests.Helpers;
using Lycia.Tests.Messages;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Lycia.Tests;

/// <summary>
/// Coverage for the coordinated compensation grammar:
/// <c>Context.MarkAsCompensated&lt;TStep&gt;(ct)</c> (root/final, terminal) and
/// <c>Context.MarkAsCompensated&lt;TStep&gt;().ThenBubbleUp(ct)</c> (intermediate, staged).
/// </summary>
[Collection(Lycia.Tests.Messages.CompensationHandlerFixtureCollection.Name)]
public class CompensationContinuationTests
{
    private static IMessageSerializer Serializer() => new NewtonsoftJsonMessageSerializer();

    private static (SagaContext<DummyEvent> Context, Mock<ISagaStore> SagaStore,
        Mock<ISagaCompensationCoordinator> Coordinator) CreateMockedContext()
    {
        var eventBusMock = new Mock<IEventBus>();
        eventBusMock.SetupGet(b => b.ApplicationId).Returns("TestApp");
        var sagaStoreMock = new Mock<ISagaStore>();
        var coordinatorMock = new Mock<ISagaCompensationCoordinator>();

        var currentStep = new DummyEvent { MessageId = Guid.NewGuid(), ParentMessageId = Guid.NewGuid() };
        var context = new SagaContext<DummyEvent>(
            Guid.NewGuid(), currentStep, typeof(CompensationContinuationTests),
            eventBusMock.Object, sagaStoreMock.Object, Mock.Of<ISagaIdGenerator>(), coordinatorMock.Object);

        return (context, sagaStoreMock, coordinatorMock);
    }

    private static (InMemorySagaStore Store, SagaCompensationCoordinator Coordinator, Mock<IEventBus> EventBus,
        IServiceProvider Provider) CreateHarness(Guid sagaId, IServiceCollection services)
    {
        services.AddSingleton<IMessageSerializer>(Serializer());
        var eventBusMock = new Mock<IEventBus>();
        eventBusMock.SetupGet(b => b.ApplicationId).Returns("TestApp");
        var sagaIdGen = new TestSagaIdGenerator(sagaId);
        var store = new InMemorySagaStore(eventBusMock.Object, sagaIdGen, Mock.Of<ISagaCompensationCoordinator>());
        services.AddSingleton<ISagaStore>(store);
        services.AddSingleton<IEventBus>(eventBusMock.Object);
        services.AddSingleton<ISagaCompensationCoordinator>(sp =>
            new SagaCompensationCoordinator(sp, sagaIdGen, sp.GetRequiredService<IMessageSerializer>()));
        var provider = services.BuildServiceProvider();
        var coordinator = (SagaCompensationCoordinator)provider.GetRequiredService<ISagaCompensationCoordinator>();
        return (store, coordinator, eventBusMock, provider);
    }

    // Token-bearing terminal form: Context.MarkAsCompensated<TStep>(ct) marks the step compensated and
    // stops there - it must not propagate to the parent. This is the recommended root/final call.
    [Fact]
    public async Task Token_MarkAsCompensated_Marks_Compensated_And_Does_Not_Propagate()
    {
        var (context, sagaStore, coordinator) = CreateMockedContext();
        using var cts = new CancellationTokenSource();

        await context.MarkAsCompensated<DummyEvent>(cts.Token);

        sagaStore.Verify(s => s.LogStepAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<Type>(),
            StepStatus.Compensated, It.IsAny<Type>(), It.IsAny<object?>(), (Exception?)null, cts.Token), Times.Once);
        coordinator.Verify(c => c.CompensateParentAsync(It.IsAny<Guid>(), It.IsAny<Type>(), It.IsAny<Type>(),
            It.IsAny<Lycia.Saga.Abstractions.Messaging.IMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // Staged form: the no-token MarkAsCompensated<TStep>() overload does nothing by itself - only
    // ThenBubbleUp(ct) executes anything, and that single token governs the whole composite operation.
    [Fact]
    public async Task NoToken_MarkAsCompensated_Does_Nothing_Until_ThenBubbleUp_Is_Awaited()
    {
        var (context, sagaStore, coordinator) = CreateMockedContext();

        var continuation = context.MarkAsCompensated<DummyEvent>();

        sagaStore.Verify(s => s.LogStepAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<Type>(),
                It.IsAny<StepStatus>(), It.IsAny<Type>(), It.IsAny<object?>(), It.IsAny<Exception?>(), It.IsAny<CancellationToken>()),
            Times.Never, "Nothing must execute before ThenBubbleUp is awaited.");
        coordinator.Verify(c => c.CompensateParentAsync(It.IsAny<Guid>(), It.IsAny<Type>(), It.IsAny<Type>(),
            It.IsAny<Lycia.Saga.Abstractions.Messaging.IMessage>(), It.IsAny<CancellationToken>()), Times.Never);

        using var cts = new CancellationTokenSource();
        await continuation.ThenBubbleUp(cts.Token);

        // ThenBubbleUp delegates to the internal bubble-up primitive, which itself both persists the
        // current step as Compensated and walks the parent lineage - see CompensateParentAsync.
        coordinator.Verify(c => c.CompensateParentAsync(It.IsAny<Guid>(), typeof(DummyEvent), It.IsAny<Type>(),
            It.IsAny<Lycia.Saga.Abstractions.Messaging.IMessage>(), cts.Token), Times.Once);
    }

    // The same terminal token governs the entire two-stage operation: only ThenBubbleUp's token is
    // observed anywhere in the chain.
    [Fact]
    public async Task ThenBubbleUp_Token_Is_The_Single_Token_For_The_Whole_Composite_Operation()
    {
        var (context, _, coordinator) = CreateMockedContext();
        using var cts = new CancellationTokenSource();

        await context.MarkAsCompensated<DummyEvent>().ThenBubbleUp(cts.Token);

        coordinator.Verify(c => c.CompensateParentAsync(It.IsAny<Guid>(), It.IsAny<Type>(), It.IsAny<Type>(),
            It.IsAny<Lycia.Saga.Abstractions.Messaging.IMessage>(), cts.Token), Times.Once);
    }

    // A pre-cancelled terminal token prevents the deferred bubble-up from running at all - cancellation
    // before the durable handoff prevents it entirely.
    [Fact]
    public async Task Cancelled_ThenBubbleUp_Token_Prevents_Propagation()
    {
        var (context, _, coordinator) = CreateMockedContext();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var continuation = context.MarkAsCompensated<DummyEvent>();
        await Assert.ThrowsAsync<OperationCanceledException>(() => continuation.ThenBubbleUp(cts.Token));

        coordinator.Verify(c => c.CompensateParentAsync(It.IsAny<Guid>(), It.IsAny<Type>(), It.IsAny<Type>(),
            It.IsAny<Lycia.Saga.Abstractions.Messaging.IMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // The no-token MarkAsCompensated<TStep>() call never performs business rollback or any framework
    // transition by itself - constructing the continuation must not touch the SagaStore or the
    // coordinator at all. ThenBubbleUp is reachable only from the object this call returns.
    [Fact]
    public void NoToken_MarkAsCompensated_Alone_Executes_Nothing()
    {
        var (context, sagaStore, coordinator) = CreateMockedContext();

        _ = context.MarkAsCompensated<DummyEvent>();

        sagaStore.VerifyNoOtherCalls();
        coordinator.VerifyNoOtherCalls();
    }

    // End-to-end through the real coordinator + a real InMemorySagaStore (no mocks on the compensation
    // path): ThenBubbleUp on a REACTIVE context actually invokes the parent's compensation handler.
    [Fact]
    public async Task ThenBubbleUp_On_A_Reactive_Context_Invokes_The_Parent_Compensation_Handler()
    {
        var fixedSagaId = Guid.NewGuid();
        var parentMessageId = Guid.NewGuid();
        var childMessageId = Guid.NewGuid();

        var parent = new DummyEvent
        {
            SagaId = fixedSagaId, MessageId = parentMessageId, ParentMessageId = Guid.Empty,
            CorrelationId = Guid.NewGuid(), Timestamp = DateTime.UtcNow, ApplicationId = "Test"
        };
        var child = new DummyEvent
        {
            SagaId = fixedSagaId, MessageId = childMessageId, ParentMessageId = parentMessageId,
            CorrelationId = parent.CorrelationId, Timestamp = DateTime.UtcNow, ApplicationId = "Test"
        };

        ParentCompensationHandler.Invocations.Clear();
        var services = new ServiceCollection();
        services.AddSingleton(new ParentCompensationHandler());
        var (store, coordinator, eventBus, _) = CreateHarness(fixedSagaId, services);

        // The parent step is already recorded as failed (as it would be after MarkAsFailed ran for it).
        await store.LogStepAsync(fixedSagaId, parentMessageId, Guid.Empty, typeof(DummyEvent), StepStatus.Failed,
            typeof(ParentCompensationHandler), parent, (SagaStepFailureInfo?)null);

        var context = new SagaContext<DummyEvent>(fixedSagaId, child, typeof(ParentCompensationHandler),
            eventBus.Object, store, new TestSagaIdGenerator(fixedSagaId), coordinator);

        await context.MarkAsCompensated<DummyEvent>().ThenBubbleUp(CancellationToken.None);

        Assert.Single(ParentCompensationHandler.Invocations);
        Assert.Equal(StepStatus.Compensated, await store.GetStepStatusAsync(fixedSagaId, childMessageId, typeof(DummyEvent), typeof(ParentCompensationHandler)));
    }

    // Idempotent retry: calling ThenBubbleUp twice for the same already-compensated step invokes the
    // parent's handler only once - CompensateParentAsync's own guard prevents the second attempt. This is
    // the duplicate/at-least-once-redelivery scenario.
    [Fact]
    public async Task ThenBubbleUp_Is_Idempotent_On_Retry_For_The_Same_Step()
    {
        var fixedSagaId = Guid.NewGuid();
        var parentMessageId = Guid.NewGuid();
        var childMessageId = Guid.NewGuid();
        var parent = new DummyEvent { SagaId = fixedSagaId, MessageId = parentMessageId, ParentMessageId = Guid.Empty };
        var child = new DummyEvent { SagaId = fixedSagaId, MessageId = childMessageId, ParentMessageId = parentMessageId };

        ParentCompensationHandler.Invocations.Clear();
        var services = new ServiceCollection();
        services.AddSingleton(new ParentCompensationHandler());
        var (store, coordinator, eventBus, _) = CreateHarness(fixedSagaId, services);
        await store.LogStepAsync(fixedSagaId, parentMessageId, Guid.Empty, typeof(DummyEvent), StepStatus.Failed,
            typeof(ParentCompensationHandler), parent, (SagaStepFailureInfo?)null);
        var context = new SagaContext<DummyEvent>(fixedSagaId, child, typeof(ParentCompensationHandler),
            eventBus.Object, store, new TestSagaIdGenerator(fixedSagaId), coordinator);

        // Same call, twice - simulating an at-least-once redelivery of the same failed-event message.
        await context.MarkAsCompensated<DummyEvent>().ThenBubbleUp(CancellationToken.None);
        await context.MarkAsCompensated<DummyEvent>().ThenBubbleUp(CancellationToken.None);

        Assert.Single(ParentCompensationHandler.Invocations);
    }

    // Exact identity, never type-based or ordering-based lookup: two contexts constructed for different
    // current steps that SHARE the same message TYPE must each propagate only to their own parent -
    // there is no caller-supplied "which message" parameter any more (the fluent form always uses the
    // exact step the context was constructed for), so this proves that identity is still never resolved
    // by type or by any global/ambiguous scan.
    [Fact]
    public async Task ThenBubbleUp_Uses_The_Exact_Current_Step_Identity_Never_Any_Step_Of_The_Same_Type()
    {
        var fixedSagaId = Guid.NewGuid();
        var parentAId = Guid.NewGuid();
        var parentBId = Guid.NewGuid();
        var parentHandlerA = new TrackingCompensationHandler();
        var parentHandlerB = new TrackingParentHandler();

        var services = new ServiceCollection();
        services.AddSingleton(parentHandlerA);
        services.AddSingleton(parentHandlerB);
        var (store, coordinator, eventBus, _) = CreateHarness(fixedSagaId, services);

        await store.LogStepAsync(fixedSagaId, parentAId, Guid.Empty, typeof(DummyEvent), StepStatus.Failed,
            typeof(TrackingCompensationHandler), new DummyEvent { SagaId = fixedSagaId, MessageId = parentAId }, (SagaStepFailureInfo?)null);
        await store.LogStepAsync(fixedSagaId, parentBId, Guid.Empty, typeof(DummyEvent), StepStatus.Failed,
            typeof(TrackingParentHandler), new DummyEvent { SagaId = fixedSagaId, MessageId = parentBId }, (SagaStepFailureInfo?)null);

        // Two children, same message TYPE (DummyEvent), different MessageIds, different parents.
        var childA = new DummyEvent { SagaId = fixedSagaId, MessageId = Guid.NewGuid(), ParentMessageId = parentAId };
        var childB = new DummyEvent { SagaId = fixedSagaId, MessageId = Guid.NewGuid(), ParentMessageId = parentBId };
        var sagaIdGen = new TestSagaIdGenerator(fixedSagaId);

        var contextA = new SagaContext<DummyEvent>(fixedSagaId, childA, typeof(TrackingCompensationHandler),
            eventBus.Object, store, sagaIdGen, coordinator);
        await contextA.MarkAsCompensated<DummyEvent>().ThenBubbleUp(CancellationToken.None);

        Assert.Single(parentHandlerA.Invocations);
        Assert.Empty(parentHandlerB.Invocations); // B must never be touched by A's propagation

        var contextB = new SagaContext<DummyEvent>(fixedSagaId, childB, typeof(TrackingParentHandler),
            eventBus.Object, store, sagaIdGen, coordinator);
        await contextB.MarkAsCompensated<DummyEvent>().ThenBubbleUp(CancellationToken.None);

        Assert.Single(parentHandlerA.Invocations); // still exactly one - unaffected by B's propagation
        Assert.Single(parentHandlerB.Invocations);
    }

    // Sibling branches: compensating one child's fluent bubble-up must not touch its sibling.
    [Fact]
    public async Task ThenBubbleUp_Does_Not_Touch_A_Sibling_Branch()
    {
        var fixedSagaId = Guid.NewGuid();
        var parentMessageId = Guid.NewGuid();
        var parentHandler = new TrackingCompensationHandler();
        var services = new ServiceCollection();
        services.AddSingleton(parentHandler);
        var (store, coordinator, eventBus, _) = CreateHarness(fixedSagaId, services);

        await store.LogStepAsync(fixedSagaId, parentMessageId, Guid.Empty, typeof(DummyEvent), StepStatus.Failed,
            typeof(TrackingCompensationHandler), new DummyEvent { SagaId = fixedSagaId, MessageId = parentMessageId }, (SagaStepFailureInfo?)null);

        var siblingC = new DummyEvent { SagaId = fixedSagaId, MessageId = Guid.NewGuid(), ParentMessageId = parentMessageId };
        await store.LogStepAsync(fixedSagaId, siblingC.MessageId, parentMessageId, typeof(DummyEvent), StepStatus.Completed,
            typeof(ChildCompensationHandler), siblingC, (SagaStepFailureInfo?)null);

        var childB = new DummyEvent { SagaId = fixedSagaId, MessageId = Guid.NewGuid(), ParentMessageId = parentMessageId };
        var contextB = new SagaContext<DummyEvent>(fixedSagaId, childB, typeof(TrackingCompensationHandler),
            eventBus.Object, store, new TestSagaIdGenerator(fixedSagaId), coordinator);
        await contextB.MarkAsCompensated<DummyEvent>().ThenBubbleUp(CancellationToken.None);

        Assert.Single(parentHandler.Invocations);
        var siblingStatus = await store.GetStepStatusAsync(fixedSagaId, siblingC.MessageId, typeof(DummyEvent), typeof(ChildCompensationHandler));
        Assert.Equal(StepStatus.Completed, siblingStatus); // untouched
        var siblingIntent = await store.GetCompensationPropagationIntentAsync(fixedSagaId, siblingC.MessageId);
        Assert.Null(siblingIntent);
    }

    // The durable propagation intent exists before the CompensationWorker ever needs to recover it - the
    // immediate attempt and CompensationWorker recovery both rely on this durable record being written
    // first, never on the immediate attempt itself succeeding.
    [Fact]
    public async Task ThenBubbleUp_Establishes_A_Durable_Intent_Before_The_Immediate_Attempt_Completes()
    {
        var fixedSagaId = Guid.NewGuid();
        var parentMessageId = Guid.NewGuid();
        var parentHandler = new TrackingCompensationHandler();
        var services = new ServiceCollection();
        services.AddSingleton(parentHandler);
        var (store, coordinator, eventBus, _) = CreateHarness(fixedSagaId, services);

        await store.LogStepAsync(fixedSagaId, parentMessageId, Guid.Empty, typeof(DummyEvent), StepStatus.Failed,
            typeof(TrackingCompensationHandler), new DummyEvent { SagaId = fixedSagaId, MessageId = parentMessageId }, (SagaStepFailureInfo?)null);

        var child = new DummyEvent { SagaId = fixedSagaId, MessageId = Guid.NewGuid(), ParentMessageId = parentMessageId };
        var context = new SagaContext<DummyEvent>(fixedSagaId, child, typeof(TrackingCompensationHandler),
            eventBus.Object, store, new TestSagaIdGenerator(fixedSagaId), coordinator);
        await context.MarkAsCompensated<DummyEvent>().ThenBubbleUp(CancellationToken.None);

        Assert.Single(parentHandler.Invocations);
        var intent = await store.GetCompensationPropagationIntentAsync(fixedSagaId, child.MessageId);
        Assert.NotNull(intent);
        Assert.Equal(CompensationPropagationStatus.Completed, intent!.Status);
    }

    // CLOSED GAP (formerly KnownGap_A; see DEVELOPERS.md "Coordinated compensation continuation" and
    // PROJECT_LEDGER.md for the durable-propagation architecture that closed it). The old design used the
    // child step's own Compensated status as the sole guard against re-running propagation, so a crash
    // between persisting Compensated and invoking the parent stranded the parent forever - a retry read
    // "already Compensated" and skipped propagation entirely. CompensateParentAsync no longer uses that
    // guard: it always re-logs Compensated (a safe no-op via step-transition validation) and then durably
    // ensures-and-claims a CompensationPropagationIntent for the edge, which is the sole authority for
    // whether propagation is still outstanding. This test reproduces the exact same pre-seeded state - the
    // child already durably Compensated, no record of the parent's own attempt - and proves the parent is
    // now invoked anyway (recovery after the durable handoff / a simulated crash).
    [Fact]
    public async Task Crash_Simulated_Between_Persisting_Compensated_And_Invoking_The_Parent_No_Longer_Strands_Propagation()
    {
        var fixedSagaId = Guid.NewGuid();
        var parentMessageId = Guid.NewGuid();
        var childMessageId = Guid.NewGuid();
        var parent = new DummyEvent { SagaId = fixedSagaId, MessageId = parentMessageId, ParentMessageId = Guid.Empty };
        var child = new DummyEvent { SagaId = fixedSagaId, MessageId = childMessageId, ParentMessageId = parentMessageId };

        ParentCompensationHandler.Invocations.Clear();
        var services = new ServiceCollection();
        services.AddSingleton(new ParentCompensationHandler());
        var (store, coordinator, eventBus, _) = CreateHarness(fixedSagaId, services);
        await store.LogStepAsync(fixedSagaId, parentMessageId, Guid.Empty, typeof(DummyEvent), StepStatus.Failed,
            typeof(ParentCompensationHandler), parent, (SagaStepFailureInfo?)null);
        // Simulates the crash: the child is already durably Compensated, exactly as CompensateParentAsync
        // would have left it, but the parent's handler never ran (no record for the parent's own attempt),
        // and - crucially - no propagation intent exists yet either (the crash happened before that fact
        // was durably recorded too).
        await store.LogStepAsync(fixedSagaId, childMessageId, parentMessageId, typeof(DummyEvent), StepStatus.Compensated,
            typeof(ParentCompensationHandler), child, (Exception?)null);
        var context = new SagaContext<DummyEvent>(fixedSagaId, child, typeof(ParentCompensationHandler),
            eventBus.Object, store, new TestSagaIdGenerator(fixedSagaId), coordinator);

        // A "retry" after the simulated crash - the same call an at-least-once redelivery would make.
        await context.MarkAsCompensated<DummyEvent>().ThenBubbleUp(CancellationToken.None);

        // The gap is closed: the parent is invoked even though the child already read as Compensated,
        // because propagation is decided by the durable intent, not by the child's own step status.
        Assert.Single(ParentCompensationHandler.Invocations);
        var intent = await store.GetCompensationPropagationIntentAsync(fixedSagaId, childMessageId);
        Assert.NotNull(intent);
        Assert.Equal(CompensationPropagationStatus.Completed, intent!.Status);
    }
}
