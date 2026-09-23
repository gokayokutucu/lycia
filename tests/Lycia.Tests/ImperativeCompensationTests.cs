// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Common.Enums;
using Lycia.Common.SagaSteps;
using Lycia.Compensating;
using Lycia.Extensions.Serialization;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Compensating;
using Lycia.Saga.Abstractions.Handlers;
using Lycia.Saga.Abstractions.Messaging;
using Lycia.Saga.Abstractions.Serializers;
using Lycia.Saga.Contexts;
using Lycia.Stores;
using Lycia.Tests.Helpers;
using Lycia.Tests.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Lycia.Tests;

/// <summary>
/// Coverage for the advanced imperative compensation API:
/// <c>Context.Compensate(failedEvent, ct)</c> / <c>Context.MarkAsCompensated&lt;TStep&gt;(ct)</c> /
/// <c>Context.BubbleUpCompensation(failedEvent, ct)</c>. The recommended, canonical form remains the
/// staged fluent grammar covered by <c>CompensationContinuationTests</c> and
/// <c>CompensationCrashInjectionTests</c>; these tests exist to prove the imperative alternative shares
/// its durable propagation implementation exactly, and that its runtime-checked ordering invariant
/// (impossible to express at compile time here, unlike the fluent form) actually holds.
/// </summary>
public class ImperativeCompensationTests
{
    private static IMessageSerializer Serializer() => new NewtonsoftJsonMessageSerializer();

    private static CompensationWorker CreateWorker(IServiceProvider provider, CompensationWorkerOptions options) =>
        new(provider.GetRequiredService<IServiceScopeFactory>(), Options.Create(options),
            NullLogger<CompensationWorker>.Instance);

    private static (InMemorySagaStore Store, SagaCompensationCoordinator Coordinator, Mock<IEventBus> EventBus,
        IServiceProvider Provider) CreateHarness(Guid sagaId, IServiceCollection services,
        IOptions<CompensationWorkerOptions>? workerOptions = null)
    {
        services.AddSingleton<IMessageSerializer>(Serializer());
        var eventBusMock = new Mock<IEventBus>();
        eventBusMock.SetupGet(b => b.ApplicationId).Returns("TestApp");
        var sagaIdGen = new TestSagaIdGenerator(sagaId);
        var store = new InMemorySagaStore(eventBusMock.Object, sagaIdGen, Mock.Of<ISagaCompensationCoordinator>());
        services.AddSingleton<ISagaStore>(store);
        services.AddSingleton<IEventBus>(eventBusMock.Object);
        services.AddSingleton<ISagaCompensationCoordinator>(sp =>
            new SagaCompensationCoordinator(sp, sagaIdGen, sp.GetRequiredService<IMessageSerializer>(), workerOptions));
        var provider = services.BuildServiceProvider();
        var coordinator = (SagaCompensationCoordinator)provider.GetRequiredService<ISagaCompensationCoordinator>();
        return (store, coordinator, eventBusMock, provider);
    }

    private static SagaContext<DummyEvent> ContextFor(DummyEvent currentStep, InMemorySagaStore store,
        IEventBus eventBus, SagaCompensationCoordinator coordinator, Guid sagaId) =>
        new(sagaId, currentStep, typeof(TrackingCompensationHandler), eventBus, store,
            new TestSagaIdGenerator(sagaId), coordinator);

    // --- Compensate(failedEvent, ct): real trigger semantics, token propagates ---

    [Fact]
    public async Task Compensate_Publishes_The_FailedEvent_And_Propagates_The_Token()
    {
        var eventBusMock = new Mock<IEventBus>();
        eventBusMock.SetupGet(b => b.ApplicationId).Returns("TestApp");
        var sagaStoreMock = new Mock<ISagaStore>();
        var currentStep = new DummyEvent { MessageId = Guid.NewGuid(), ParentMessageId = Guid.NewGuid() };
        var context = new SagaContext<DummyEvent>(Guid.NewGuid(), currentStep, typeof(ImperativeCompensationTests),
            eventBusMock.Object, sagaStoreMock.Object, Mock.Of<ISagaIdGenerator>(), Mock.Of<ISagaCompensationCoordinator>());
        using var cts = new CancellationTokenSource();
        var failed = new DummyFailedEvent { MessageId = Guid.NewGuid() };

        await context.Compensate(failed, cts.Token);

        // Compensate publishes/triggers reactive compensation via the outgoing pipeline - it does not by
        // itself mark any step compensated or touch the durable propagation coordinator.
        eventBusMock.Verify(b => b.Publish(It.IsAny<DummyFailedEvent>(), It.IsAny<Type?>(), It.IsAny<Guid?>(), cts.Token),
            Times.Once);
        sagaStoreMock.VerifyNoOtherCalls();
    }

    // --- MarkAsCompensated<T>(ct): independently valid; root compensation stops there ---

    [Fact]
    public async Task Root_Compensation_Can_Stop_After_MarkAsCompensated_Alone()
    {
        var sagaId = Guid.NewGuid();
        var rootStep = new DummyEvent { SagaId = sagaId, MessageId = Guid.NewGuid(), ParentMessageId = Guid.Empty };
        var (store, coordinator, eventBus, _) = CreateHarness(sagaId, new ServiceCollection());
        var context = ContextFor(rootStep, store, eventBus.Object, coordinator, sagaId);

        await context.MarkAsCompensated<DummyEvent>(CancellationToken.None);

        var status = await store.GetStepStatusAsync(sagaId, rootStep.MessageId, typeof(DummyEvent), typeof(TrackingCompensationHandler));
        Assert.Equal(StepStatus.Compensated, status);
        var intent = await store.GetCompensationPropagationIntentAsync(sagaId, rootStep.MessageId);
        Assert.Null(intent); // no propagation was ever requested - and none should exist
    }

    // --- BubbleUpCompensation ordering validation ---

    [Fact]
    public async Task BubbleUpCompensation_Before_MarkAsCompensated_Throws_And_Creates_No_Intent()
    {
        var sagaId = Guid.NewGuid();
        var parentMessageId = Guid.NewGuid();
        var childStep = new DummyEvent { SagaId = sagaId, MessageId = Guid.NewGuid(), ParentMessageId = parentMessageId };
        var (store, coordinator, eventBus, _) = CreateHarness(sagaId, new ServiceCollection());
        var context = ContextFor(childStep, store, eventBus.Object, coordinator, sagaId);

        // Application programming error: BubbleUpCompensation called without a preceding MarkAsCompensated.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.BubbleUpCompensation(childStep, CancellationToken.None));

        Assert.Contains("MarkAsCompensated", ex.Message);
        var intent = await store.GetCompensationPropagationIntentAsync(sagaId, childStep.MessageId);
        Assert.Null(intent); // nothing durable was created - not silently propagated, not silently marked
        var status = await store.GetStepStatusAsync(sagaId, childStep.MessageId, typeof(DummyEvent), typeof(TrackingCompensationHandler));
        Assert.Equal(StepStatus.None, status); // BubbleUpCompensation never marks the step itself
    }

    [Fact]
    public async Task BubbleUpCompensation_With_An_Unrelated_Event_Throws_And_Mutates_Nothing()
    {
        var sagaId = Guid.NewGuid();
        var parentMessageId = Guid.NewGuid();
        var childStep = new DummyEvent { SagaId = sagaId, MessageId = Guid.NewGuid(), ParentMessageId = parentMessageId };
        var unrelated = new DummyEvent { SagaId = sagaId, MessageId = Guid.NewGuid(), ParentMessageId = parentMessageId };
        var (store, coordinator, eventBus, _) = CreateHarness(sagaId, new ServiceCollection());
        var context = ContextFor(childStep, store, eventBus.Object, coordinator, sagaId);

        await context.MarkAsCompensated<DummyEvent>(CancellationToken.None);

        // The context was constructed for childStep; passing a different message (even one that never
        // existed at all) must be rejected deterministically, never resolved by falling back to type or
        // to "the current step".
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.BubbleUpCompensation(unrelated, CancellationToken.None));
        Assert.Contains(unrelated.MessageId.ToString(), ex.Message);

        var unrelatedIntent = await store.GetCompensationPropagationIntentAsync(sagaId, unrelated.MessageId);
        Assert.Null(unrelatedIntent);
        var childIntent = await store.GetCompensationPropagationIntentAsync(sagaId, childStep.MessageId);
        Assert.Null(childIntent); // the actual current step's propagation was never requested either
    }

    // --- Same-message-type ambiguity: two steps share a type but have distinct MessageIds/ParentMessageIds ---

    [Fact]
    public async Task BubbleUpCompensation_Uses_The_Exact_MessageId_Never_Any_Step_Of_The_Same_Type()
    {
        var sagaId = Guid.NewGuid();
        var parentAId = Guid.NewGuid();
        var parentBId = Guid.NewGuid();
        var parentHandlerA = new TrackingCompensationHandler();
        var parentHandlerB = new TrackingParentHandler();

        var services = new ServiceCollection();
        services.AddSingleton(parentHandlerA);
        services.AddSingleton(parentHandlerB);
        var (store, coordinator, eventBus, _) = CreateHarness(sagaId, services);

        await store.LogStepAsync(sagaId, parentAId, Guid.Empty, typeof(DummyEvent), StepStatus.Failed,
            typeof(TrackingCompensationHandler), new DummyEvent { SagaId = sagaId, MessageId = parentAId }, (SagaStepFailureInfo?)null);
        await store.LogStepAsync(sagaId, parentBId, Guid.Empty, typeof(DummyEvent), StepStatus.Failed,
            typeof(TrackingParentHandler), new DummyEvent { SagaId = sagaId, MessageId = parentBId }, (SagaStepFailureInfo?)null);

        // Two children, same message TYPE (DummyEvent), different MessageIds, different parents.
        var childA = new DummyEvent { SagaId = sagaId, MessageId = Guid.NewGuid(), ParentMessageId = parentAId };
        var childB = new DummyEvent { SagaId = sagaId, MessageId = Guid.NewGuid(), ParentMessageId = parentBId };

        var contextA = ContextFor(childA, store, eventBus.Object, coordinator, sagaId);
        await contextA.MarkAsCompensated<DummyEvent>(CancellationToken.None);
        await contextA.BubbleUpCompensation(childA, CancellationToken.None);

        Assert.Single(parentHandlerA.Invocations);
        Assert.Empty(parentHandlerB.Invocations); // B must never be touched by A's propagation

        var contextB = ContextFor(childB, store, eventBus.Object, coordinator, sagaId);
        await contextB.MarkAsCompensated<DummyEvent>(CancellationToken.None);
        await contextB.BubbleUpCompensation(childB, CancellationToken.None);

        Assert.Single(parentHandlerA.Invocations); // still exactly one - unaffected by B's propagation
        Assert.Single(parentHandlerB.Invocations);
    }

    // --- Sibling branches: compensating one child's imperative bubble-up must not touch its sibling ---

    [Fact]
    public async Task BubbleUpCompensation_Does_Not_Touch_A_Sibling_Branch()
    {
        var sagaId = Guid.NewGuid();
        var parentMessageId = Guid.NewGuid();
        var parentHandler = new TrackingCompensationHandler();
        var services = new ServiceCollection();
        services.AddSingleton(parentHandler);
        var (store, coordinator, eventBus, _) = CreateHarness(sagaId, services);

        await store.LogStepAsync(sagaId, parentMessageId, Guid.Empty, typeof(DummyEvent), StepStatus.Failed,
            typeof(TrackingCompensationHandler), new DummyEvent { SagaId = sagaId, MessageId = parentMessageId }, (SagaStepFailureInfo?)null);

        var siblingC = new DummyEvent { SagaId = sagaId, MessageId = Guid.NewGuid(), ParentMessageId = parentMessageId };
        await store.LogStepAsync(sagaId, siblingC.MessageId, parentMessageId, typeof(DummyEvent), StepStatus.Completed,
            typeof(ChildCompensationHandler), siblingC, (SagaStepFailureInfo?)null);

        var childB = new DummyEvent { SagaId = sagaId, MessageId = Guid.NewGuid(), ParentMessageId = parentMessageId };
        var contextB = ContextFor(childB, store, eventBus.Object, coordinator, sagaId);
        await contextB.MarkAsCompensated<DummyEvent>(CancellationToken.None);
        await contextB.BubbleUpCompensation(childB, CancellationToken.None);

        Assert.Single(parentHandler.Invocations);
        var siblingStatus = await store.GetStepStatusAsync(sagaId, siblingC.MessageId, typeof(DummyEvent), typeof(ChildCompensationHandler));
        Assert.Equal(StepStatus.Completed, siblingStatus); // untouched
        var siblingIntent = await store.GetCompensationPropagationIntentAsync(sagaId, siblingC.MessageId);
        Assert.Null(siblingIntent);
    }

    // --- Durable propagation + idempotency ---

    [Fact]
    public async Task BubbleUpCompensation_Establishes_A_Durable_Intent_And_Attempts_The_Parent_Immediately()
    {
        var sagaId = Guid.NewGuid();
        var parentMessageId = Guid.NewGuid();
        var parentHandler = new TrackingCompensationHandler();
        var services = new ServiceCollection();
        services.AddSingleton(parentHandler);
        var (store, coordinator, eventBus, _) = CreateHarness(sagaId, services);

        await store.LogStepAsync(sagaId, parentMessageId, Guid.Empty, typeof(DummyEvent), StepStatus.Failed,
            typeof(TrackingCompensationHandler), new DummyEvent { SagaId = sagaId, MessageId = parentMessageId }, (SagaStepFailureInfo?)null);

        var child = new DummyEvent { SagaId = sagaId, MessageId = Guid.NewGuid(), ParentMessageId = parentMessageId };
        var context = ContextFor(child, store, eventBus.Object, coordinator, sagaId);
        await context.MarkAsCompensated<DummyEvent>(CancellationToken.None);
        await context.BubbleUpCompensation(child, CancellationToken.None);

        Assert.Single(parentHandler.Invocations);
        var intent = await store.GetCompensationPropagationIntentAsync(sagaId, child.MessageId);
        Assert.NotNull(intent);
        Assert.Equal(CompensationPropagationStatus.Completed, intent!.Status);
    }

    [Fact]
    public async Task Repeated_BubbleUpCompensation_Is_Idempotent_At_The_Logical_Propagation_Level()
    {
        var sagaId = Guid.NewGuid();
        var parentMessageId = Guid.NewGuid();
        var parentHandler = new TrackingCompensationHandler();
        var services = new ServiceCollection();
        services.AddSingleton(parentHandler);
        var (store, coordinator, eventBus, _) = CreateHarness(sagaId, services);

        await store.LogStepAsync(sagaId, parentMessageId, Guid.Empty, typeof(DummyEvent), StepStatus.Failed,
            typeof(TrackingCompensationHandler), new DummyEvent { SagaId = sagaId, MessageId = parentMessageId }, (SagaStepFailureInfo?)null);

        var child = new DummyEvent { SagaId = sagaId, MessageId = Guid.NewGuid(), ParentMessageId = parentMessageId };
        var context = ContextFor(child, store, eventBus.Object, coordinator, sagaId);
        await context.MarkAsCompensated<DummyEvent>(CancellationToken.None);

        // Simulates an at-least-once redelivery of the same failed-event message calling BubbleUpCompensation twice.
        await context.BubbleUpCompensation(child, CancellationToken.None);
        await context.BubbleUpCompensation(child, CancellationToken.None);

        Assert.Single(parentHandler.Invocations); // not twice - the second call finds AlreadyCompleted and stops
    }

    // --- Crash recovery: durable handoff committed, immediate attempt never ran ---

    [Fact]
    public async Task CompensationWorker_Recovers_An_Imperative_BubbleUpCompensation_Crash()
    {
        var sagaId = Guid.NewGuid();
        var parentMessageId = Guid.NewGuid();
        var childMessageId = Guid.NewGuid();
        var options = new CompensationWorkerOptions { RecoveryTimeout = TimeSpan.FromMilliseconds(20) };
        var parentHandler = new TrackingCompensationHandler();
        var services = new ServiceCollection();
        services.AddSingleton(parentHandler);
        var (store, _, eventBus, provider) = CreateHarness(sagaId, services, Options.Create(options));

        await store.LogStepAsync(sagaId, parentMessageId, Guid.Empty, typeof(DummyEvent), StepStatus.Failed,
            typeof(TrackingCompensationHandler), new DummyEvent { SagaId = sagaId, MessageId = parentMessageId }, (SagaStepFailureInfo?)null);
        await store.LogStepAsync(sagaId, childMessageId, parentMessageId, typeof(DummyEvent), StepStatus.Compensated,
            typeof(ChildCompensationHandler), new DummyEvent { SagaId = sagaId, MessageId = childMessageId, ParentMessageId = parentMessageId },
            (SagaStepFailureInfo?)null);

        // Simulates: BubbleUpCompensation's durable claim committed (MarkAsCompensated already happened,
        // per the pre-seeded Compensated status above), then the process crashed before the immediate
        // parent attempt ran.
        var claim = await store.EnsureAndClaimCompensationPropagationAsync(sagaId, childMessageId, parentMessageId,
            "dead-inline-owner", options.RecoveryTimeout, options.MaxAttempts);
        Assert.Equal(CompensationPropagationClaimOutcome.Claimed, claim.Outcome);
        Assert.Empty(parentHandler.Invocations);

        await Task.Delay(TimeSpan.FromMilliseconds(80));
        var worker = CreateWorker(provider, options);
        var result = await worker.RunOnceAsync();

        Assert.Equal(1, result.Succeeded);
        Assert.Single(parentHandler.Invocations);
    }

    // --- Cancellation semantics ---

    [Fact]
    public async Task Cancellation_Before_BubbleUpCompensation_Prevents_The_Durable_Handoff()
    {
        var sagaId = Guid.NewGuid();
        var parentMessageId = Guid.NewGuid();
        var child = new DummyEvent { SagaId = sagaId, MessageId = Guid.NewGuid(), ParentMessageId = parentMessageId };
        var (store, coordinator, eventBus, _) = CreateHarness(sagaId, new ServiceCollection());
        var context = ContextFor(child, store, eventBus.Object, coordinator, sagaId);
        await context.MarkAsCompensated<DummyEvent>(CancellationToken.None);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => context.BubbleUpCompensation(child, cts.Token));

        var intent = await store.GetCompensationPropagationIntentAsync(sagaId, child.MessageId);
        Assert.Null(intent); // the claim never happened - cancellation before handoff prevents it entirely
    }

    [Fact]
    public async Task Cancellation_After_The_Durable_Handoff_Does_Not_Erase_The_Propagation_Requirement()
    {
        var sagaId = Guid.NewGuid();
        var parentMessageId = Guid.NewGuid();
        var childMessageId = Guid.NewGuid();
        var options = new CompensationWorkerOptions { RecoveryTimeout = TimeSpan.FromMilliseconds(20) };
        var parentHandler = new TrackingCompensationHandler();
        var services = new ServiceCollection();
        services.AddSingleton(parentHandler);
        var (store, _, eventBus, provider) = CreateHarness(sagaId, services, Options.Create(options));

        await store.LogStepAsync(sagaId, parentMessageId, Guid.Empty, typeof(DummyEvent), StepStatus.Failed,
            typeof(TrackingCompensationHandler), new DummyEvent { SagaId = sagaId, MessageId = parentMessageId }, (SagaStepFailureInfo?)null);
        await store.LogStepAsync(sagaId, childMessageId, parentMessageId, typeof(DummyEvent), StepStatus.Compensated,
            typeof(ChildCompensationHandler), new DummyEvent { SagaId = sagaId, MessageId = childMessageId, ParentMessageId = parentMessageId },
            (SagaStepFailureInfo?)null);

        // The durable claim itself commits and is never cancellable once requested; simulate cancellation
        // of the immediate attempt that would normally follow it by simply never making that attempt here.
        var claim = await store.EnsureAndClaimCompensationPropagationAsync(sagaId, childMessageId, parentMessageId,
            "inline:cancelled-attempt", options.RecoveryTimeout, options.MaxAttempts);
        Assert.Equal(CompensationPropagationClaimOutcome.Claimed, claim.Outcome);
        Assert.Empty(parentHandler.Invocations);

        await Task.Delay(TimeSpan.FromMilliseconds(80));
        var worker = CreateWorker(provider, options);
        var result = await worker.RunOnceAsync();

        Assert.Equal(1, result.Succeeded);
        Assert.Single(parentHandler.Invocations);
    }

    // --- Fluent and imperative converge on one propagation implementation ---

    [Fact]
    public async Task Fluent_And_Imperative_Forms_Produce_The_Same_Durable_Propagation_Artifact_Shape()
    {
        var sagaId = Guid.NewGuid();
        var parentAId = Guid.NewGuid();
        var parentBId = Guid.NewGuid();
        var fluentParentHandler = new TrackingCompensationHandler();
        var imperativeParentHandler = new TrackingParentHandler();
        var services = new ServiceCollection();
        services.AddSingleton(fluentParentHandler);
        services.AddSingleton(imperativeParentHandler);
        var (store, coordinator, eventBus, _) = CreateHarness(sagaId, services);

        await store.LogStepAsync(sagaId, parentAId, Guid.Empty, typeof(DummyEvent), StepStatus.Failed,
            typeof(TrackingCompensationHandler), new DummyEvent { SagaId = sagaId, MessageId = parentAId }, (SagaStepFailureInfo?)null);
        await store.LogStepAsync(sagaId, parentBId, Guid.Empty, typeof(DummyEvent), StepStatus.Failed,
            typeof(TrackingParentHandler), new DummyEvent { SagaId = sagaId, MessageId = parentBId }, (SagaStepFailureInfo?)null);

        // Fluent: ContinueCompensation().ThenMarkAsCompensated<T>().ThenBubbleUp(ct)
        var childFluent = new DummyEvent { SagaId = sagaId, MessageId = Guid.NewGuid(), ParentMessageId = parentAId };
        var fluentContext = ContextFor(childFluent, store, eventBus.Object, coordinator, sagaId);
        await fluentContext.ContinueCompensation().ThenMarkAsCompensated<DummyEvent>().ThenBubbleUp(CancellationToken.None);

        // Imperative: MarkAsCompensated<T>(ct) then BubbleUpCompensation(failedEvent, ct)
        var childImperative = new DummyEvent { SagaId = sagaId, MessageId = Guid.NewGuid(), ParentMessageId = parentBId };
        var imperativeContext = ContextFor(childImperative, store, eventBus.Object, coordinator, sagaId);
        await imperativeContext.MarkAsCompensated<DummyEvent>(CancellationToken.None);
        await imperativeContext.BubbleUpCompensation(childImperative, CancellationToken.None);

        Assert.Single(fluentParentHandler.Invocations);
        Assert.Single(imperativeParentHandler.Invocations);

        var fluentIntent = await store.GetCompensationPropagationIntentAsync(sagaId, childFluent.MessageId);
        var imperativeIntent = await store.GetCompensationPropagationIntentAsync(sagaId, childImperative.MessageId);
        Assert.NotNull(fluentIntent);
        Assert.NotNull(imperativeIntent);
        // Same terminal status, same identity shape (SagaId + ChildMessageId), same completed attempt
        // count - both call chains reached the exact same durable artifact through the same implementation.
        Assert.Equal(CompensationPropagationStatus.Completed, fluentIntent!.Status);
        Assert.Equal(CompensationPropagationStatus.Completed, imperativeIntent!.Status);
        Assert.Equal(1, fluentIntent.AttemptCount);
        Assert.Equal(1, imperativeIntent.AttemptCount);
    }
}

/// <summary>Minimal <see cref="Lycia.Saga.Abstractions.Messaging.IFailedEventBase"/> for testing <c>Context.Compensate</c>.</summary>
public sealed class DummyFailedEvent : DummyEvent, IFailedEventBase
{
    public string Reason { get; set; } = string.Empty;
}
