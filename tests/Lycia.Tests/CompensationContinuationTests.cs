// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Common.Enums;
using Lycia.Common.SagaSteps;
using Lycia.Compensating;
using Lycia.Extensions.Serialization;
using Lycia.Saga;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Serializers;
using Lycia.Saga.Contexts;
using Lycia.Stores;
using Lycia.Tests.Helpers;
using Lycia.Tests.Messages;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Lycia.Tests;

/// <summary>
/// Coverage for the coordinated compensation continuation fluent API:
/// <c>Context.ContinueCompensation().ThenMarkAsCompensated&lt;TStep&gt;(...)[.ThenBubbleUp(...)]</c>.
/// </summary>
[Collection(Lycia.Tests.Messages.CompensationHandlerFixtureCollection.Name)]
public class CompensationContinuationTests
{
    private static (SagaContext<DummyEvent> Context, Mock<ISagaStore> SagaStore,
        Mock<ISagaCompensationCoordinator> Coordinator) CreateContext()
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

    // Two-stage terminal form: ContinueCompensation().ThenMarkAsCompensated<TStep>(ct) marks the step
    // compensated and stops there - it must not propagate to the parent.
    [Fact]
    public async Task Two_Stage_ThenMarkAsCompensated_Marks_Compensated_And_Does_Not_Propagate()
    {
        var (context, sagaStore, coordinator) = CreateContext();
        using var cts = new CancellationTokenSource();

        await context.ContinueCompensation().ThenMarkAsCompensated<DummyEvent>(cts.Token);

        sagaStore.Verify(s => s.LogStepAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<Type>(),
            StepStatus.Compensated, It.IsAny<Type>(), It.IsAny<object?>(), (Exception?)null, cts.Token), Times.Once);
        coordinator.Verify(c => c.CompensateParentAsync(It.IsAny<Guid>(), It.IsAny<Type>(), It.IsAny<Type>(),
            It.IsAny<Lycia.Saga.Abstractions.Messaging.IMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // Three-stage form: the no-token ThenMarkAsCompensated<TStep>() overload does nothing by itself - only
    // ThenBubbleUp(ct) executes anything, and that single token governs the whole composite operation.
    [Fact]
    public async Task NoToken_ThenMarkAsCompensated_Does_Nothing_Until_ThenBubbleUp_Is_Awaited()
    {
        var (context, sagaStore, coordinator) = CreateContext();

        var continuation = context.ContinueCompensation().ThenMarkAsCompensated<DummyEvent>();

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

    // The same terminal token governs the entire three-stage operation: only ThenBubbleUp's token is
    // observed anywhere in the chain.
    [Fact]
    public async Task ThenBubbleUp_Token_Is_The_Single_Token_For_The_Whole_Composite_Operation()
    {
        var (context, _, coordinator) = CreateContext();
        using var cts = new CancellationTokenSource();

        await context.ContinueCompensation().ThenMarkAsCompensated<DummyEvent>().ThenBubbleUp(cts.Token);

        coordinator.Verify(c => c.CompensateParentAsync(It.IsAny<Guid>(), It.IsAny<Type>(), It.IsAny<Type>(),
            It.IsAny<Lycia.Saga.Abstractions.Messaging.IMessage>(), cts.Token), Times.Once);
    }

    // A pre-cancelled terminal token prevents the deferred bubble-up from running at all.
    [Fact]
    public async Task Cancelled_ThenBubbleUp_Token_Prevents_Propagation()
    {
        var (context, _, coordinator) = CreateContext();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var continuation = context.ContinueCompensation().ThenMarkAsCompensated<DummyEvent>();
        await Assert.ThrowsAsync<OperationCanceledException>(() => continuation.ThenBubbleUp(cts.Token));

        coordinator.Verify(c => c.CompensateParentAsync(It.IsAny<Guid>(), It.IsAny<Type>(), It.IsAny<Type>(),
            It.IsAny<Lycia.Saga.Abstractions.Messaging.IMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ContinueCompensation() never performs business rollback or any framework transition by itself -
    // constructing the continuation must not touch the SagaStore or the coordinator at all.
    [Fact]
    public void ContinueCompensation_Alone_Executes_Nothing()
    {
        var (context, sagaStore, coordinator) = CreateContext();

        _ = context.ContinueCompensation();

        sagaStore.VerifyNoOtherCalls();
        coordinator.VerifyNoOtherCalls();
    }

    // End-to-end through the real coordinator + a real InMemorySagaStore (no mocks on the compensation
    // path): ThenBubbleUp on a REACTIVE context actually invokes the parent's compensation handler. This
    // is the fix for the previous no-op StepSpecificSagaContextAdapter<T> bubble-up stub - before the fix,
    // this exact call silently did nothing for every reactive saga.
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
        services.AddSingleton<IMessageSerializer, NewtonsoftJsonMessageSerializer>();
        var eventBusMock = new Mock<IEventBus>();
        eventBusMock.SetupGet(b => b.ApplicationId).Returns("TestApp");
        var sagaIdGen = new TestSagaIdGenerator(fixedSagaId);
        var dummyCoordinator = Mock.Of<ISagaCompensationCoordinator>();
        var store = new InMemorySagaStore(eventBusMock.Object, sagaIdGen, dummyCoordinator);

        // The parent step is already recorded as failed (as it would be after MarkAsFailed ran for it).
        await store.LogStepAsync(fixedSagaId, parentMessageId, Guid.Empty, typeof(DummyEvent), StepStatus.Failed,
            typeof(ParentCompensationHandler), parent, (SagaStepFailureInfo?)null);

        services.AddSingleton<ISagaStore>(store);
        services.AddSingleton<IEventBus>(eventBusMock.Object);
        services.AddSingleton<ParentCompensationHandler>();

        var provider = services.BuildServiceProvider();
        var coordinator = new SagaCompensationCoordinator(provider, sagaIdGen, provider.GetRequiredService<IMessageSerializer>());

        var context = new SagaContext<DummyEvent>(fixedSagaId, child, typeof(ParentCompensationHandler),
            eventBusMock.Object, store, sagaIdGen, coordinator);

        await context.ContinueCompensation().ThenMarkAsCompensated<DummyEvent>().ThenBubbleUp(CancellationToken.None);

        Assert.Single(ParentCompensationHandler.Invocations);
        Assert.Equal(StepStatus.Compensated, await store.GetStepStatusAsync(fixedSagaId, childMessageId, typeof(DummyEvent), typeof(ParentCompensationHandler)));
    }

    // Idempotent retry: calling ThenBubbleUp twice for the same already-compensated step invokes the
    // parent's handler only once - CompensateParentAsync's own guard prevents the second attempt.
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
        services.AddSingleton<IMessageSerializer, NewtonsoftJsonMessageSerializer>();
        var eventBusMock = new Mock<IEventBus>();
        eventBusMock.SetupGet(b => b.ApplicationId).Returns("TestApp");
        var sagaIdGen = new TestSagaIdGenerator(fixedSagaId);
        var store = new InMemorySagaStore(eventBusMock.Object, sagaIdGen, Mock.Of<ISagaCompensationCoordinator>());
        await store.LogStepAsync(fixedSagaId, parentMessageId, Guid.Empty, typeof(DummyEvent), StepStatus.Failed,
            typeof(ParentCompensationHandler), parent, (SagaStepFailureInfo?)null);
        services.AddSingleton<ISagaStore>(store);
        services.AddSingleton<IEventBus>(eventBusMock.Object);
        services.AddSingleton<ParentCompensationHandler>();
        var provider = services.BuildServiceProvider();
        var coordinator = new SagaCompensationCoordinator(provider, sagaIdGen, provider.GetRequiredService<IMessageSerializer>());
        var context = new SagaContext<DummyEvent>(fixedSagaId, child, typeof(ParentCompensationHandler),
            eventBusMock.Object, store, sagaIdGen, coordinator);

        // Same call, twice - simulating an at-least-once redelivery of the same failed-event message.
        await context.ContinueCompensation().ThenMarkAsCompensated<DummyEvent>().ThenBubbleUp(CancellationToken.None);
        await context.ContinueCompensation().ThenMarkAsCompensated<DummyEvent>().ThenBubbleUp(CancellationToken.None);

        Assert.Single(ParentCompensationHandler.Invocations);
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
    // now invoked anyway.
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
        services.AddSingleton<IMessageSerializer, NewtonsoftJsonMessageSerializer>();
        var eventBusMock = new Mock<IEventBus>();
        eventBusMock.SetupGet(b => b.ApplicationId).Returns("TestApp");
        var sagaIdGen = new TestSagaIdGenerator(fixedSagaId);
        var store = new InMemorySagaStore(eventBusMock.Object, sagaIdGen, Mock.Of<ISagaCompensationCoordinator>());
        await store.LogStepAsync(fixedSagaId, parentMessageId, Guid.Empty, typeof(DummyEvent), StepStatus.Failed,
            typeof(ParentCompensationHandler), parent, (SagaStepFailureInfo?)null);
        // Simulates the crash: the child is already durably Compensated, exactly as CompensateParentAsync
        // would have left it, but the parent's handler never ran (no record for the parent's own attempt),
        // and - crucially - no propagation intent exists yet either (the crash happened before that fact
        // was durably recorded too).
        // Matches the exact overload CompensateParentAsync itself uses for this transition, so a genuine
        // at-least-once retry re-logging the identical Compensated status for this step is recognized as
        // idempotent rather than misread as a differing-payload conflict.
        await store.LogStepAsync(fixedSagaId, childMessageId, parentMessageId, typeof(DummyEvent), StepStatus.Compensated,
            typeof(ParentCompensationHandler), child, (Exception?)null);
        services.AddSingleton<ISagaStore>(store);
        services.AddSingleton<IEventBus>(eventBusMock.Object);
        services.AddSingleton<ParentCompensationHandler>();
        var provider = services.BuildServiceProvider();
        var coordinator = new SagaCompensationCoordinator(provider, sagaIdGen, provider.GetRequiredService<IMessageSerializer>());
        var context = new SagaContext<DummyEvent>(fixedSagaId, child, typeof(ParentCompensationHandler),
            eventBusMock.Object, store, sagaIdGen, coordinator);

        // A "retry" after the simulated crash - the same call an at-least-once redelivery would make.
        await context.ContinueCompensation().ThenMarkAsCompensated<DummyEvent>().ThenBubbleUp(CancellationToken.None);

        // The gap is closed: the parent is invoked even though the child already read as Compensated,
        // because propagation is decided by the durable intent, not by the child's own step status.
        Assert.Single(ParentCompensationHandler.Invocations);
        var intent = await store.GetCompensationPropagationIntentAsync(fixedSagaId, childMessageId);
        Assert.NotNull(intent);
        Assert.Equal(CompensationPropagationStatus.Completed, intent!.Status);
    }
}
