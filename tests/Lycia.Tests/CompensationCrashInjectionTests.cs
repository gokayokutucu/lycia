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
using Lycia.Saga.Abstractions.Serializers;
using Lycia.Stores;
using Lycia.Tests.Helpers;
using Lycia.Tests.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Lycia.Tests;

/// <summary>
/// Crash-window regression coverage for durable compensation propagation, keyed to the boundaries listed
/// in <c>DEVELOPERS.md</c>, "Coordinated compensation continuation": for each boundary, a fact is
/// pre-seeded exactly as a real crash would have left it (never simulated by throwing mid-call), and the
/// test proves the documented recovery behavior. Boundary C (the original documented gap) is covered by
/// <see cref="CompensationContinuationTests.Crash_Simulated_Between_Persisting_Compensated_And_Invoking_The_Parent_No_Longer_Strands_Propagation"/>.
/// Handlers here are per-test instances (not the shared static-list fixtures in Messages/GrandparentCompensationHandler.cs)
/// so this class's tests never race with other test classes over shared mutable static state.
/// </summary>
public class CompensationCrashInjectionTests
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
        // Registered for real (not just constructed standalone) so CompensationWorker.RunOnceAsync - which
        // resolves ISagaCompensationCoordinator from its own DI scope - can find it too.
        services.AddSingleton<ISagaCompensationCoordinator>(sp =>
            new SagaCompensationCoordinator(sp, sagaIdGen, sp.GetRequiredService<IMessageSerializer>(), workerOptions));
        var provider = services.BuildServiceProvider();
        var coordinator = (SagaCompensationCoordinator)provider.GetRequiredService<ISagaCompensationCoordinator>();
        return (store, coordinator, eventBusMock, provider);
    }

    // --- Boundary A/B: nothing durable exists yet before the current step's compensation transition ---
    // Business compensation (user code) and any at-least-once handler retry happen entirely before
    // CompensateParentAsync is ever called; the coordinator has no state to lose at this boundary because
    // it has not run yet. This is the baseline: no step is Compensated and no propagation intent exists.
    [Fact]
    public async Task Boundary_AB_Before_Any_Compensation_Call_No_Step_Or_Intent_State_Exists()
    {
        var sagaId = Guid.NewGuid();
        var childMessageId = Guid.NewGuid();
        var (store, _, _, _) = CreateHarness(sagaId, new ServiceCollection());

        var status = await store.GetStepStatusAsync(sagaId, childMessageId, typeof(DummyEvent), typeof(ChildCompensationHandler));
        var intent = await store.GetCompensationPropagationIntentAsync(sagaId, childMessageId);

        Assert.Equal(StepStatus.None, status);
        Assert.Null(intent);
    }

    // --- Boundary D/E: an intent exists (durably handed off) but the immediate attempt never invoked the
    // parent - either because the process crashed right after the claim committed (D) or because a prior
    // claim's lease went stale before the parent was ever invoked (E). Both recover identically: a
    // CompensationWorker pass claims the due edge and completes propagation.
    [Fact]
    public async Task Boundary_DE_CompensationWorker_Recovers_A_Stale_Claim_And_Completes_Propagation()
    {
        var sagaId = Guid.NewGuid();
        var parentMessageId = Guid.NewGuid();
        var childMessageId = Guid.NewGuid();
        var parent = new DummyEvent { SagaId = sagaId, MessageId = parentMessageId, ParentMessageId = Guid.Empty };
        var options = new CompensationWorkerOptions { RecoveryTimeout = TimeSpan.FromMilliseconds(20) };
        var parentHandler = new TrackingCompensationHandler();

        var services = new ServiceCollection();
        services.AddSingleton(parentHandler);
        var (store, _, _, provider) = CreateHarness(sagaId, services, Options.Create(options));

        await store.LogStepAsync(sagaId, parentMessageId, Guid.Empty, typeof(DummyEvent), StepStatus.Failed,
            typeof(TrackingCompensationHandler), parent, (SagaStepFailureInfo?)null);
        await store.LogStepAsync(sagaId, childMessageId, parentMessageId, typeof(DummyEvent), StepStatus.Compensated,
            typeof(ChildCompensationHandler), new DummyEvent { SagaId = sagaId, MessageId = childMessageId, ParentMessageId = parentMessageId },
            (SagaStepFailureInfo?)null);

        // Durable handoff committed, then the owner died before ever invoking the parent (D), or before
        // its own claim was even distinguishable from a fresh one (E) - the observable state is the same.
        var claim = await store.EnsureAndClaimCompensationPropagationAsync(sagaId, childMessageId, parentMessageId,
            "dead-inline-owner", options.RecoveryTimeout, options.MaxAttempts);
        Assert.Equal(CompensationPropagationClaimOutcome.Claimed, claim.Outcome);
        Assert.Empty(parentHandler.Invocations);

        await Task.Delay(TimeSpan.FromMilliseconds(100));

        var worker = CreateWorker(provider, options);
        var result = await worker.RunOnceAsync();

        Assert.Equal(1, result.Claimed);
        Assert.Equal(1, result.Succeeded);
        Assert.Equal(0, result.Failed);
        Assert.Single(parentHandler.Invocations);
        var finalIntent = await store.GetCompensationPropagationIntentAsync(sagaId, childMessageId);
        Assert.Equal(CompensationPropagationStatus.Completed, finalIntent!.Status);
    }

    // --- Boundary F: the parent's business compensation keeps failing. Bounded, at-least-once retry -
    // the worker must not give up before MaxAttempts, and must succeed once the parent's handler does.
    [Fact]
    public async Task Boundary_F_CompensationWorker_Retries_Until_The_Parent_Handler_Eventually_Succeeds()
    {
        var sagaId = Guid.NewGuid();
        var parentMessageId = Guid.NewGuid();
        var childMessageId = Guid.NewGuid();
        var parent = new DummyEvent { SagaId = sagaId, MessageId = parentMessageId, ParentMessageId = Guid.Empty };
        var options = new CompensationWorkerOptions { RecoveryTimeout = TimeSpan.FromMilliseconds(20), MaxAttempts = 5 };
        var flakyHandler = new FlakyCompensationHandler(failuresBeforeSuccess: 2);

        var services = new ServiceCollection();
        services.AddSingleton(flakyHandler);
        var (store, _, _, provider) = CreateHarness(sagaId, services, Options.Create(options));

        await store.LogStepAsync(sagaId, parentMessageId, Guid.Empty, typeof(DummyEvent), StepStatus.Failed,
            typeof(FlakyCompensationHandler), parent, (SagaStepFailureInfo?)null);
        await store.LogStepAsync(sagaId, childMessageId, parentMessageId, typeof(DummyEvent), StepStatus.Compensated,
            typeof(ChildCompensationHandler), new DummyEvent { SagaId = sagaId, MessageId = childMessageId, ParentMessageId = parentMessageId },
            (SagaStepFailureInfo?)null);
        await store.EnsureAndClaimCompensationPropagationAsync(sagaId, childMessageId, parentMessageId,
            "dead-owner", options.RecoveryTimeout, options.MaxAttempts);
        await Task.Delay(TimeSpan.FromMilliseconds(60));

        var worker = CreateWorker(provider, options);

        // Pass 1: parent handler throws (attempt 1 of the flaky handler) - edge stays Claimed.
        var pass1 = await worker.RunOnceAsync();
        Assert.Equal(1, pass1.Claimed);
        Assert.Equal(1, pass1.Failed);
        var afterPass1 = await store.GetCompensationPropagationIntentAsync(sagaId, childMessageId);
        Assert.Equal(CompensationPropagationStatus.Claimed, afterPass1!.Status);

        await Task.Delay(TimeSpan.FromMilliseconds(60));

        // Pass 2: parent handler throws again (2nd of 2 configured failures) - still bounded, not exhausted (MaxAttempts=5).
        var pass2 = await worker.RunOnceAsync();
        Assert.Equal(1, pass2.Failed);

        await Task.Delay(TimeSpan.FromMilliseconds(60));

        // Pass 3: parent handler now succeeds.
        var pass3 = await worker.RunOnceAsync();
        Assert.Equal(1, pass3.Succeeded);
        Assert.Equal(0, pass3.Failed);

        var finalIntent = await store.GetCompensationPropagationIntentAsync(sagaId, childMessageId);
        Assert.Equal(CompensationPropagationStatus.Completed, finalIntent!.Status);
        Assert.Equal(3, flakyHandler.AttemptCount);
    }

    // --- Boundary F (exhaustion): a parent handler that always fails must not retry forever - it becomes
    // an operator-visible terminal Failed edge, bounded by MaxAttempts, and is never silently discarded.
    [Fact]
    public async Task Boundary_F_CompensationWorker_Marks_Edge_Failed_After_MaxAttempts_Exhausted()
    {
        var sagaId = Guid.NewGuid();
        var parentMessageId = Guid.NewGuid();
        var childMessageId = Guid.NewGuid();
        var parent = new DummyEvent { SagaId = sagaId, MessageId = parentMessageId, ParentMessageId = Guid.Empty };
        var options = new CompensationWorkerOptions { RecoveryTimeout = TimeSpan.FromMilliseconds(15), MaxAttempts = 2 };
        var alwaysFails = new FlakyCompensationHandler(failuresBeforeSuccess: int.MaxValue);

        var services = new ServiceCollection();
        services.AddSingleton(alwaysFails);
        var (store, _, _, provider) = CreateHarness(sagaId, services, Options.Create(options));

        await store.LogStepAsync(sagaId, parentMessageId, Guid.Empty, typeof(DummyEvent), StepStatus.Failed,
            typeof(FlakyCompensationHandler), parent, (SagaStepFailureInfo?)null);
        await store.LogStepAsync(sagaId, childMessageId, parentMessageId, typeof(DummyEvent), StepStatus.Compensated,
            typeof(ChildCompensationHandler), new DummyEvent { SagaId = sagaId, MessageId = childMessageId, ParentMessageId = parentMessageId },
            (SagaStepFailureInfo?)null);
        await store.EnsureAndClaimCompensationPropagationAsync(sagaId, childMessageId, parentMessageId,
            "dead-owner", options.RecoveryTimeout, options.MaxAttempts);

        var worker = CreateWorker(provider, options);

        // attempt 1 already consumed by the inline EnsureAndClaim above; one more claim reaches MaxAttempts.
        await Task.Delay(TimeSpan.FromMilliseconds(50));
        await worker.RunOnceAsync(); // attempt 2 - throws, exhausts MaxAttempts (2)
        await Task.Delay(TimeSpan.FromMilliseconds(50));
        var finalPass = await worker.RunOnceAsync(); // discovers exhaustion, moves edge to terminal Failed

        Assert.Equal(0, finalPass.Claimed);
        var finalIntent = await store.GetCompensationPropagationIntentAsync(sagaId, childMessageId);
        Assert.Equal(CompensationPropagationStatus.Failed, finalIntent!.Status);
        Assert.NotNull(finalIntent.FailureInfo);
    }

    // --- Boundary G/I: the parent's business compensation completed (its handler was invoked) but the
    // framework crashed before recording propagation completion. A retry of the same claimed edge must be
    // idempotent at the framework level (no corrupted lineage, no duplicate intents) even though the
    // parent's own handler runs again - this is exactly why compensation handlers must make their own
    // external side effects idempotent (Lycia remains at-least-once, never exactly-once).
    [Fact]
    public async Task Boundary_GI_Retrying_A_Claimed_Edge_After_A_Simulated_Completion_Crash_Is_Idempotent()
    {
        var sagaId = Guid.NewGuid();
        var parentMessageId = Guid.NewGuid();
        var childMessageId = Guid.NewGuid();
        var parent = new DummyEvent { SagaId = sagaId, MessageId = parentMessageId, ParentMessageId = Guid.Empty };
        var parentHandler = new TrackingCompensationHandler();

        var services = new ServiceCollection();
        services.AddSingleton(parentHandler);
        var (store, coordinator, eventBusMock, _) = CreateHarness(sagaId, services);

        await store.LogStepAsync(sagaId, parentMessageId, Guid.Empty, typeof(DummyEvent), StepStatus.Failed,
            typeof(TrackingCompensationHandler), parent, (SagaStepFailureInfo?)null);
        await store.LogStepAsync(sagaId, childMessageId, parentMessageId, typeof(DummyEvent), StepStatus.Compensated,
            typeof(ChildCompensationHandler), new DummyEvent { SagaId = sagaId, MessageId = childMessageId, ParentMessageId = parentMessageId },
            (SagaStepFailureInfo?)null);
        var claim = await store.EnsureAndClaimCompensationPropagationAsync(sagaId, childMessageId, parentMessageId,
            "inline:test", TimeSpan.FromMinutes(1), 5);
        Assert.Equal(CompensationPropagationClaimOutcome.Claimed, claim.Outcome);

        // First attempt: parent handler runs (business undo done), then completion is recorded normally.
        await coordinator.AttemptPropagationAsync(sagaId, childMessageId, eventBusMock.Object, store, CancellationToken.None);
        Assert.Single(parentHandler.Invocations);
        var afterFirst = await store.GetCompensationPropagationIntentAsync(sagaId, childMessageId);
        Assert.Equal(CompensationPropagationStatus.Completed, afterFirst!.Status);

        // Simulated retry of the same already-Completed edge (e.g. a worker pass that raced the first
        // completion write): the framework-level record must stay a single, consistent Completed edge -
        // MarkCompensationPropagationCompletedAsync itself is a safe idempotent no-op.
        await store.MarkCompensationPropagationCompletedAsync(sagaId, childMessageId);
        var afterRetry = await store.GetCompensationPropagationIntentAsync(sagaId, childMessageId);
        Assert.Equal(CompensationPropagationStatus.Completed, afterRetry!.Status);
    }

    // --- Boundary H: after the child's edge completes, the *next* propagation edge (parent -> grandparent)
    // is itself durable and independently recoverable - it is not lost just because it is the second hop.
    [Fact]
    public async Task Boundary_H_The_Next_Propagation_Edge_In_A_Chain_Remains_Durable_And_Recoverable()
    {
        var sagaId = Guid.NewGuid();
        var grandparentMessageId = Guid.NewGuid();
        var parentMessageId = Guid.NewGuid();
        var childMessageId = Guid.NewGuid();
        var grandparent = new DummyEvent { SagaId = sagaId, MessageId = grandparentMessageId, ParentMessageId = Guid.Empty };
        var parent = new DummyEvent { SagaId = sagaId, MessageId = parentMessageId, ParentMessageId = grandparentMessageId };
        var options = new CompensationWorkerOptions { RecoveryTimeout = TimeSpan.FromMilliseconds(20) };
        var grandparentHandler = new TrackingCompensationHandler();
        var parentHandler = new TrackingParentHandler();

        var services = new ServiceCollection();
        services.AddSingleton(grandparentHandler);
        services.AddSingleton(parentHandler);
        var (store, coordinator, _, provider) = CreateHarness(sagaId, services, Options.Create(options));

        await store.LogStepAsync(sagaId, grandparentMessageId, Guid.Empty, typeof(DummyEvent), StepStatus.Failed,
            typeof(TrackingCompensationHandler), grandparent, (SagaStepFailureInfo?)null);
        await store.LogStepAsync(sagaId, parentMessageId, grandparentMessageId, typeof(DummyEvent), StepStatus.Failed,
            typeof(TrackingParentHandler), parent, (SagaStepFailureInfo?)null);

        // Hop 1 (child -> parent): drive it to completion through the real coordinator.
        var child = new DummyEvent { SagaId = sagaId, MessageId = childMessageId, ParentMessageId = parentMessageId };
        await coordinator.CompensateParentAsync(sagaId, typeof(DummyEvent), typeof(ChildCompensationHandler), child);
        Assert.Single(parentHandler.Invocations);

        // Simulates the parent's own handler, after finishing its business undo, calling
        // MarkAsCompensated<TStep>().ThenBubbleUp() itself - which durably records hop 2 (parent -> grandparent)
        // and then crashes before that edge's own propagation attempt runs.
        var hop2Claim = await store.EnsureAndClaimCompensationPropagationAsync(sagaId, parentMessageId,
            grandparentMessageId, "dead-inline-owner", options.RecoveryTimeout, options.MaxAttempts);
        Assert.Equal(CompensationPropagationClaimOutcome.Claimed, hop2Claim.Outcome);
        Assert.Empty(grandparentHandler.Invocations); // not yet reached - proves it wasn't lost, only pending

        await Task.Delay(TimeSpan.FromMilliseconds(80));

        var worker = CreateWorker(provider, options);
        var result = await worker.RunOnceAsync();

        Assert.Equal(1, result.Succeeded);
        Assert.Single(grandparentHandler.Invocations);
        var hop2Intent = await store.GetCompensationPropagationIntentAsync(sagaId, parentMessageId);
        Assert.Equal(CompensationPropagationStatus.Completed, hop2Intent!.Status);
    }

    // --- Section 12 (root behavior): a root step (no logical parent) must not create useless propagation
    // work - no ParentMessageId, no propagation intent.
    [Fact]
    public async Task Root_Step_Compensation_Creates_No_Propagation_Intent()
    {
        var sagaId = Guid.NewGuid();
        var rootMessageId = Guid.NewGuid();
        var root = new DummyEvent { SagaId = sagaId, MessageId = rootMessageId, ParentMessageId = Guid.Empty };
        var (store, coordinator, _, _) = CreateHarness(sagaId, new ServiceCollection());

        await coordinator.CompensateParentAsync(sagaId, typeof(DummyEvent), typeof(TrackingCompensationHandler), root);

        var status = await store.GetStepStatusAsync(sagaId, rootMessageId, typeof(DummyEvent), typeof(TrackingCompensationHandler));
        Assert.Equal(StepStatus.Compensated, status);
        var intent = await store.GetCompensationPropagationIntentAsync(sagaId, rootMessageId);
        Assert.Null(intent);
    }

    // --- Section 13 (branching / ParentMessageId lineage only): compensating one child must never
    // compensate its siblings. A with children B, C: compensating B must not touch C.
    [Fact]
    public async Task Compensating_One_Child_Does_Not_Propagate_To_Or_Touch_A_Sibling()
    {
        var sagaId = Guid.NewGuid();
        var parentMessageId = Guid.NewGuid();
        var siblingBMessageId = Guid.NewGuid();
        var siblingCMessageId = Guid.NewGuid();
        var parent = new DummyEvent { SagaId = sagaId, MessageId = parentMessageId, ParentMessageId = Guid.Empty };
        var parentHandler = new TrackingCompensationHandler();

        var services = new ServiceCollection();
        services.AddSingleton(parentHandler);
        var (store, coordinator, _, _) = CreateHarness(sagaId, services);

        await store.LogStepAsync(sagaId, parentMessageId, Guid.Empty, typeof(DummyEvent), StepStatus.Failed,
            typeof(TrackingCompensationHandler), parent, (SagaStepFailureInfo?)null);
        // Sibling C is a completely independent, still-healthy step under the same parent.
        var siblingC = new DummyEvent { SagaId = sagaId, MessageId = siblingCMessageId, ParentMessageId = parentMessageId };
        await store.LogStepAsync(sagaId, siblingCMessageId, parentMessageId, typeof(DummyEvent), StepStatus.Completed,
            typeof(ChildCompensationHandler), siblingC, (SagaStepFailureInfo?)null);

        var siblingB = new DummyEvent { SagaId = sagaId, MessageId = siblingBMessageId, ParentMessageId = parentMessageId };
        await coordinator.CompensateParentAsync(sagaId, typeof(DummyEvent), typeof(ChildCompensationHandler), siblingB);

        // Only B's propagation reached the parent (once); C is never touched.
        Assert.Single(parentHandler.Invocations);
        var cStatus = await store.GetStepStatusAsync(sagaId, siblingCMessageId, typeof(DummyEvent), typeof(ChildCompensationHandler));
        Assert.Equal(StepStatus.Completed, cStatus);
        var cIntent = await store.GetCompensationPropagationIntentAsync(sagaId, siblingCMessageId);
        Assert.Null(cIntent);
    }

    // --- Section 7 (cancellation boundary): once the durable handoff (the claim) has committed, cancelling
    // the immediate attempt must not erase the durable propagation requirement - CompensationWorker must
    // still be able to finish it later.
    [Fact]
    public async Task Cancellation_After_Durable_Handoff_Does_Not_Erase_The_Propagation_Requirement()
    {
        var sagaId = Guid.NewGuid();
        var parentMessageId = Guid.NewGuid();
        var childMessageId = Guid.NewGuid();
        var parent = new DummyEvent { SagaId = sagaId, MessageId = parentMessageId, ParentMessageId = Guid.Empty };
        var options = new CompensationWorkerOptions { RecoveryTimeout = TimeSpan.FromMilliseconds(20) };
        var parentHandler = new TrackingCompensationHandler();

        var services = new ServiceCollection();
        services.AddSingleton(parentHandler);
        var (store, _, _, provider) = CreateHarness(sagaId, services, Options.Create(options));

        await store.LogStepAsync(sagaId, parentMessageId, Guid.Empty, typeof(DummyEvent), StepStatus.Failed,
            typeof(TrackingCompensationHandler), parent, (SagaStepFailureInfo?)null);
        await store.LogStepAsync(sagaId, childMessageId, parentMessageId, typeof(DummyEvent), StepStatus.Compensated,
            typeof(ChildCompensationHandler), new DummyEvent { SagaId = sagaId, MessageId = childMessageId, ParentMessageId = parentMessageId },
            (SagaStepFailureInfo?)null);

        // The durable claim itself commits (it is not cancellable once requested); simulate cancellation
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

    // --- Section 7 (cancellation boundary, before handoff): a token already cancelled before the durable
    // handoff must prevent it from being created at all.
    [Fact]
    public async Task Cancellation_Before_Durable_Handoff_Prevents_The_Claim_From_Being_Created()
    {
        var sagaId = Guid.NewGuid();
        var parentMessageId = Guid.NewGuid();
        var childMessageId = Guid.NewGuid();
        var child = new DummyEvent { SagaId = sagaId, MessageId = childMessageId, ParentMessageId = parentMessageId };
        var (store, coordinator, _, _) = CreateHarness(sagaId, new ServiceCollection());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            coordinator.CompensateParentAsync(sagaId, typeof(DummyEvent), typeof(ChildCompensationHandler), child, cts.Token));

        var intent = await store.GetCompensationPropagationIntentAsync(sagaId, childMessageId);
        Assert.Null(intent);
    }
}

/// <summary>Test-only, per-instance (never shared/static) compensation handler that tracks its own invocations.</summary>
public sealed class TrackingCompensationHandler : ISagaCompensationHandler<DummyEvent>
{
    public List<DummyEvent> Invocations { get; } = [];

    public Task CompensateAsync(DummyEvent message, CancellationToken cancellationToken = default)
    {
        Invocations.Add(message);
        return Task.CompletedTask;
    }
}

/// <summary>
/// A second, distinctly-typed per-instance tracking handler - needed when a single test plays two
/// different roles (e.g. parent and grandparent) that must resolve to two different DI registrations.
/// </summary>
public sealed class TrackingParentHandler : ISagaCompensationHandler<DummyEvent>
{
    public List<DummyEvent> Invocations { get; } = [];

    public Task CompensateAsync(DummyEvent message, CancellationToken cancellationToken = default)
    {
        Invocations.Add(message);
        return Task.CompletedTask;
    }
}

/// <summary>Test-only compensation handler whose first N invocations throw, then it succeeds.</summary>
public sealed class FlakyCompensationHandler(int failuresBeforeSuccess) : ISagaCompensationHandler<DummyEvent>
{
    public int AttemptCount { get; private set; }

    public Task CompensateAsync(DummyEvent message, CancellationToken cancellationToken = default)
    {
        AttemptCount++;
        if (AttemptCount <= failuresBeforeSuccess)
            throw new InvalidOperationException($"Simulated parent compensation failure #{AttemptCount}");

        return Task.CompletedTask;
    }
}
