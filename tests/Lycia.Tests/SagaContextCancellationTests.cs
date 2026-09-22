// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Common.Enums;
using Lycia.Extensions.Serialization;
using Lycia.Outbox;
using Lycia.Persistence.InMemory;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Messaging;
using Lycia.Saga.Abstractions.Outbox;
using Lycia.Saga.Abstractions.Scheduling;
using Lycia.Saga.Contexts;
using Lycia.Saga.Messaging;
using Lycia.Tests.Messages;
using Moq;
using Newtonsoft.Json;

namespace Lycia.Tests;

/// <summary>
/// Focused coverage for CancellationToken consistency across standalone saga-context operations,
/// deferred tracked/composite operations, and compensation transitions.
/// </summary>
public class SagaContextCancellationTests
{
    private static (SagaContext<OrderCreatedEvent> Context, Mock<IEventBus> EventBus,
        Mock<ISagaCompensationCoordinator> Coordinator, Mock<IMessageScheduler> Scheduler)
        CreateContext()
    {
        var eventBusMock = new Mock<IEventBus>();
        eventBusMock.SetupGet(b => b.ApplicationId).Returns("TestApp");

        var sagaStore = Mock.Of<ISagaStore>();
        var sagaIdGenerator = Mock.Of<ISagaIdGenerator>();
        var coordinatorMock = new Mock<ISagaCompensationCoordinator>();
        var schedulerMock = new Mock<IMessageScheduler>();

        var currentStep = new OrderCreatedEvent { OrderId = Guid.NewGuid() };
        var context = new SagaContext<OrderCreatedEvent>(
            Guid.NewGuid(), currentStep, typeof(SagaContextCancellationTests),
            eventBusMock.Object, sagaStore, sagaIdGenerator, coordinatorMock.Object, schedulerMock.Object);

        return (context, eventBusMock, coordinatorMock, schedulerMock);
    }

    // 1. Standalone Send propagates CancellationToken.
    [Fact]
    public async Task Send_Standalone_Propagates_CancellationToken()
    {
        var (context, eventBus, _, _) = CreateContext();
        using var cts = new CancellationTokenSource();

        await context.Send(new CreateOrderCommand(), cts.Token);

        eventBus.Verify(b => b.Send(It.IsAny<CreateOrderCommand>(), It.IsAny<Type>(), It.IsAny<Guid>(), cts.Token), Times.Once);
    }

    // 2. Standalone Publish propagates CancellationToken.
    [Fact]
    public async Task Publish_Standalone_Propagates_CancellationToken()
    {
        var (context, eventBus, _, _) = CreateContext();
        using var cts = new CancellationTokenSource();

        await context.Publish(new OrderCreatedEvent(), cts.Token);

        eventBus.Verify(b => b.Publish(It.IsAny<OrderCreatedEvent>(), It.IsAny<Type>(), It.IsAny<Guid>(), cts.Token), Times.Once);
    }

    // 3. Standalone Respond propagates CancellationToken.
    [Fact]
    public async Task Respond_Standalone_Propagates_CancellationToken()
    {
        var (context, eventBus, _, _) = CreateContext();
        using var cts = new CancellationTokenSource();
        var request = new CreateOrderCommand();
        var response = new OrderCreatedResponse();

        await context.Respond(request, response, cts.Token);

        eventBus.Verify(b => b.Respond(request, response, It.IsAny<Type>(), It.IsAny<Guid>(), cts.Token), Times.Once);
    }

    // 4. Standalone Schedule propagates CancellationToken.
    [Fact]
    public async Task Schedule_Standalone_Propagates_CancellationToken()
    {
        var (context, _, _, scheduler) = CreateContext();
        using var cts = new CancellationTokenSource();
        scheduler.Setup(s => s.ScheduleAsync(It.IsAny<OrderCreatedEvent>(), It.IsAny<OrderCreatedEvent>(),
                It.IsAny<Type>(), It.IsAny<Guid>(), ScheduleDelay.FiveSeconds, null, cts.Token))
            .ReturnsAsync(Guid.NewGuid());

        await context.Schedule(new OrderCreatedEvent(), ScheduleDelay.FiveSeconds, cts.Token);

        scheduler.Verify(s => s.ScheduleAsync(It.IsAny<OrderCreatedEvent>(), It.IsAny<OrderCreatedEvent>(),
            It.IsAny<Type>(), It.IsAny<Guid>(), ScheduleDelay.FiveSeconds, null, cts.Token), Times.Once);
    }

    // 6. MarkAsFailed propagates CancellationToken to the compensation coordinator.
    [Fact]
    public async Task MarkAsFailed_Propagates_CancellationToken()
    {
        var (context, _, coordinator, _) = CreateContext();
        using var cts = new CancellationTokenSource();

        await context.MarkAsFailed<OrderCreatedEvent>(cts.Token);

        coordinator.Verify(c => c.CompensateAsync(It.IsAny<Guid>(), typeof(OrderCreatedEvent), It.IsAny<Type>(),
            It.IsAny<OrderCreatedEvent>(), It.IsAny<Common.SagaSteps.SagaStepFailureInfo>(), cts.Token), Times.Once);
    }

    // MarkAsComplete accepts/propagates a CancellationToken (item 5).
    [Fact]
    public async Task MarkAsComplete_Accepts_CancellationToken()
    {
        var (context, _, _, _) = CreateContext();
        using var cts = new CancellationTokenSource();

        await context.MarkAsComplete<OrderCreatedEvent>(cts.Token);
    }

    // 7. MarkAsCancelled accepts a CancellationToken (new symmetric overload).
    [Fact]
    public async Task MarkAsCancelled_Accepts_CancellationToken()
    {
        var (context, _, _, _) = CreateContext();
        using var cts = new CancellationTokenSource();

        await context.MarkAsCancelled<OrderCreatedEvent>(cancellationToken: cts.Token);
    }

    // 8. MarkAsCompensated accepts a CancellationToken (previously missing entirely).
    [Fact]
    public async Task MarkAsCompensated_Accepts_CancellationToken()
    {
        var (context, _, _, _) = CreateContext();
        using var cts = new CancellationTokenSource();

        await context.MarkAsCompensated<OrderCreatedEvent>(cts.Token);
    }

    // 9. MarkAsCompensationFailed accepts a CancellationToken (previously missing entirely).
    [Fact]
    public async Task MarkAsCompensationFailed_Accepts_CancellationToken()
    {
        var (context, _, _, _) = CreateContext();
        using var cts = new CancellationTokenSource();

        await context.MarkAsCompensationFailed<OrderCreatedEvent>(cts.Token);
    }

    // 10 & 11. SendWithTracking defers Send until the terminal method executes, and the terminal token
    // reaches the deferred Send. This is the preferred fluent shape.
    [Fact]
    public async Task SendWithTracking_Defers_Send_And_Terminal_Token_Reaches_It()
    {
        var (context, eventBus, _, _) = CreateContext();
        using var cts = new CancellationTokenSource();

        var tracked = context.SendWithTracking(new CreateOrderCommand());
        eventBus.Verify(b => b.Send(It.IsAny<CreateOrderCommand>(), It.IsAny<Type>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never, "Send must not execute before the terminal method is awaited.");

        await tracked.ThenMarkAsComplete(cts.Token);

        eventBus.Verify(b => b.Send(It.IsAny<CreateOrderCommand>(), It.IsAny<Type>(), It.IsAny<Guid>(), cts.Token), Times.Once);
    }

    // 12. PublishWithTracking: same deferred-execution and terminal-token model.
    [Fact]
    public async Task PublishWithTracking_Terminal_Token_Reaches_Publish()
    {
        var (context, eventBus, _, _) = CreateContext();
        using var cts = new CancellationTokenSource();

        await context.PublishWithTracking(new OrderCreatedEvent()).ThenMarkAsComplete(cts.Token);

        eventBus.Verify(b => b.Publish(It.IsAny<OrderCreatedEvent>(), It.IsAny<Type>(), It.IsAny<Guid>(), cts.Token), Times.Once);
    }

    // 13. RespondWithTracking: same deferred-execution and terminal-token model.
    [Fact]
    public async Task RespondWithTracking_Terminal_Token_Reaches_Respond()
    {
        var (context, eventBus, _, _) = CreateContext();
        using var cts = new CancellationTokenSource();
        var request = new CreateOrderCommand();
        var response = new OrderCreatedResponse();

        await context.RespondWithTracking(request, response).ThenMarkAsComplete(cts.Token);

        eventBus.Verify(b => b.Respond(request, response, It.IsAny<Type>(), It.IsAny<Guid>(), cts.Token), Times.Once);
    }

    [Fact]
    public async Task Tracked_Send_Publish_And_Respond_Capture_Outbox_Semantics_Before_Transition()
    {
        var eventBus = new Mock<IEventBus>();
        eventBus.SetupGet(bus => bus.ApplicationId).Returns("TestApp");
        var store = new InMemoryOutboxStore();
        var serializer = new NewtonsoftJsonMessageSerializer();
        var pipeline = new OutboxOutgoingMessagePipeline(store, serializer);
        var current = new OrderCreatedEvent { OrderId = Guid.NewGuid() };
        var context = new SagaContext<OrderCreatedEvent>(Guid.NewGuid(), current,
            typeof(SagaContextCancellationTests), eventBus.Object, Mock.Of<ISagaStore>(),
            Mock.Of<ISagaIdGenerator>(), Mock.Of<ISagaCompensationCoordinator>(),
            Mock.Of<IMessageScheduler>(), pipeline);
        var command = new ReserveInventoryCommand();
        var published = new OrderCreatedEvent();
        var request = new CreateOrderCommand { ResponseEndpoint = "requesting-app" };
        var response = new OrderCreatedResponse();

        await context.SendWithTracking(command).ThenMarkAsComplete();
        await context.PublishWithTracking(published).ThenMarkAsComplete();
        await context.RespondWithTracking(request, response).ThenMarkAsComplete();

        Assert.Equal(OutboxOperationKind.Send, ReadEnvelope(await store.GetByMessageIdAsync(command.MessageId)).Operation);
        Assert.Equal(OutboxOperationKind.Publish, ReadEnvelope(await store.GetByMessageIdAsync(published.MessageId)).Operation);
        Assert.Equal(OutboxOperationKind.Respond, ReadEnvelope(await store.GetByMessageIdAsync(response.MessageId)).Operation);
        eventBus.Verify(bus => bus.Send(It.IsAny<ReserveInventoryCommand>(), It.IsAny<Type>(), It.IsAny<Guid>(),
            It.IsAny<CancellationToken>()), Times.Never);
        eventBus.Verify(bus => bus.Publish(It.IsAny<OrderCreatedEvent>(), It.IsAny<Type>(), It.IsAny<Guid>(),
            It.IsAny<CancellationToken>()), Times.Never);
        eventBus.Verify(bus => bus.Respond(It.IsAny<CreateOrderCommand>(), It.IsAny<OrderCreatedResponse>(),
            It.IsAny<Type>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static OutboxEnvelope ReadEnvelope(OutboxMessage? message) =>
        JsonConvert.DeserializeObject<OutboxEnvelope>(Assert.IsType<OutboxMessage>(message).Payload)!;

    // 14. ScheduleWithTracking: same deferred-execution and terminal-token model.
    [Fact]
    public async Task ScheduleWithTracking_Terminal_Token_Reaches_Schedule()
    {
        var (context, _, _, scheduler) = CreateContext();
        using var cts = new CancellationTokenSource();
        scheduler.Setup(s => s.ScheduleAsync(It.IsAny<OrderCreatedEvent>(), It.IsAny<OrderCreatedEvent>(),
                It.IsAny<Type>(), It.IsAny<Guid>(), ScheduleDelay.FiveSeconds, null, cts.Token))
            .ReturnsAsync(Guid.NewGuid());

        await context.ScheduleWithTracking(new OrderCreatedEvent(), ScheduleDelay.FiveSeconds)
            .ThenMarkAsComplete(cts.Token);

        scheduler.Verify(s => s.ScheduleAsync(It.IsAny<OrderCreatedEvent>(), It.IsAny<OrderCreatedEvent>(),
            It.IsAny<Type>(), It.IsAny<Guid>(), ScheduleDelay.FiveSeconds, null, cts.Token), Times.Once);
    }

    // 15. An already-cancelled terminal token prevents the deferred operation from executing.
    [Fact]
    public async Task Cancelled_Terminal_Token_Prevents_Deferred_Send()
    {
        var (context, eventBus, _, _) = CreateContext();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var tracked = context.SendWithTracking(new CreateOrderCommand());

        await Assert.ThrowsAsync<OperationCanceledException>(() => tracked.ThenMarkAsComplete(cts.Token));
        eventBus.Verify(b => b.Send(It.IsAny<CreateOrderCommand>(), It.IsAny<Type>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // 16. A failure in the deferred outgoing operation prevents the saga-step transition from running:
    // RunAsync awaits `operation` before `transition`, so an exception thrown from Send propagates and
    // MarkAsComplete's underlying LogStepAsync is never reached.
    [Fact]
    public async Task Deferred_Operation_Failure_Prevents_The_Transition()
    {
        var eventBusMock = new Mock<IEventBus>();
        eventBusMock.SetupGet(b => b.ApplicationId).Returns("TestApp");
        eventBusMock.Setup(b => b.Send(It.IsAny<CreateOrderCommand>(), It.IsAny<Type>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("transport unavailable"));

        var sagaStoreMock = new Mock<ISagaStore>();
        var currentStep = new OrderCreatedEvent { OrderId = Guid.NewGuid() };
        var context = new SagaContext<OrderCreatedEvent>(
            Guid.NewGuid(), currentStep, typeof(SagaContextCancellationTests),
            eventBusMock.Object, sagaStoreMock.Object, Mock.Of<ISagaIdGenerator>(),
            Mock.Of<ISagaCompensationCoordinator>(), Mock.Of<IMessageScheduler>());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.SendWithTracking(new CreateOrderCommand()).ThenMarkAsComplete());

        sagaStoreMock.Verify(s => s.LogStepAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<Type>(),
                It.IsAny<StepStatus>(), It.IsAny<Type>(), It.IsAny<object?>(), (Common.SagaSteps.SagaStepFailureInfo?)null, It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Cancellation behavior is consistent across all four WithTracking entry points: none of them accept a
    // token, and a pre-cancelled terminal token prevents each deferred operation the same way.
    [Theory]
    [InlineData("Publish")]
    [InlineData("Respond")]
    [InlineData("Schedule")]
    public async Task Cancelled_Terminal_Token_Prevents_Every_Deferred_WithTracking_Operation(string kind)
    {
        var (context, eventBus, _, scheduler) = CreateContext();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        ISagaStepFluent tracked = kind switch
        {
            "Publish" => context.PublishWithTracking(new OrderCreatedEvent()),
            "Respond" => context.RespondWithTracking(new CreateOrderCommand(), new OrderCreatedResponse()),
            "Schedule" => context.ScheduleWithTracking(new OrderCreatedEvent(), ScheduleDelay.FiveSeconds),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        await Assert.ThrowsAsync<OperationCanceledException>(() => tracked.ThenMarkAsComplete(cts.Token));

        eventBus.Verify(b => b.Publish(It.IsAny<OrderCreatedEvent>(), It.IsAny<Type>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        eventBus.Verify(b => b.Respond(It.IsAny<CreateOrderCommand>(), It.IsAny<OrderCreatedResponse>(), It.IsAny<Type>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        scheduler.Verify(s => s.ScheduleAsync(It.IsAny<IMessage>(), It.IsAny<IMessage>(), It.IsAny<Type>(), It.IsAny<Guid>(),
            It.IsAny<ScheduleDelay>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // --- Explicit generic terminal step selection --------------------------------------------------
    // The saga step being transitioned (TStep) is intentionally decoupled from the outgoing tracked
    // message's type; the tests below use a different message for each on purpose.

    private static (SagaContext<OrderCreatedEvent> Context, Mock<IEventBus> EventBus,
        Mock<ISagaStore> SagaStore, Mock<IMessageScheduler> Scheduler)
        CreateContextWithStore()
    {
        var eventBusMock = new Mock<IEventBus>();
        eventBusMock.SetupGet(b => b.ApplicationId).Returns("TestApp");

        var sagaStoreMock = new Mock<ISagaStore>();
        var sagaIdGenerator = Mock.Of<ISagaIdGenerator>();
        var coordinator = Mock.Of<ISagaCompensationCoordinator>();
        var schedulerMock = new Mock<IMessageScheduler>();

        var currentStep = new OrderCreatedEvent { OrderId = Guid.NewGuid() };
        var context = new SagaContext<OrderCreatedEvent>(
            Guid.NewGuid(), currentStep, typeof(SagaContextCancellationTests),
            eventBusMock.Object, sagaStoreMock.Object, sagaIdGenerator, coordinator, schedulerMock.Object);

        return (context, eventBusMock, sagaStoreMock, schedulerMock);
    }

    // 1 & 5: SendWithTracking(...).ThenMarkAsComplete<TStep>(ct) transitions TStep, never the outgoing
    // command type (ReserveInventoryCommand here) and never the context's own current step by accident.
    [Fact]
    public async Task SendWithTracking_ThenMarkAsComplete_Generic_Transitions_Explicit_Step()
    {
        var (context, eventBus, sagaStore, _) = CreateContextWithStore();
        using var cts = new CancellationTokenSource();

        await context.SendWithTracking(new ReserveInventoryCommand())
            .ThenMarkAsComplete<CreateOrderCommand>(cts.Token);

        eventBus.Verify(b => b.Send(It.IsAny<ReserveInventoryCommand>(), It.IsAny<Type>(), It.IsAny<Guid>(), cts.Token), Times.Once);
        // The saga context always logs against the step it was constructed for (OrderCreatedEvent);
        // TStep on ThenMarkAsComplete is the call-site-documented intent, not a lookup key.
        Assert.Single(sagaStore.Invocations);
        var invocation = sagaStore.Invocations[0];
        Assert.Equal("LogStepAsync", invocation.Method.Name);
        Assert.Equal(typeof(OrderCreatedEvent), invocation.Arguments[3]);
    }

    // 2: PublishWithTracking(...).ThenMarkAsComplete<TStep>(ct).
    [Fact]
    public async Task PublishWithTracking_ThenMarkAsComplete_Generic_Transitions_Explicit_Step()
    {
        var (context, eventBus, _, _) = CreateContext();
        using var cts = new CancellationTokenSource();

        await context.PublishWithTracking(new OrderCreatedEvent())
            .ThenMarkAsComplete<CreateOrderCommand>(cts.Token);

        eventBus.Verify(b => b.Publish(It.IsAny<OrderCreatedEvent>(), It.IsAny<Type>(), It.IsAny<Guid>(), cts.Token), Times.Once);
    }

    // 3: RespondWithTracking(...).ThenMarkAsComplete<TStep>(ct).
    [Fact]
    public async Task RespondWithTracking_ThenMarkAsComplete_Generic_Transitions_Explicit_Step()
    {
        var (context, eventBus, _, _) = CreateContext();
        using var cts = new CancellationTokenSource();
        var request = new CreateOrderCommand();
        var response = new OrderCreatedResponse();

        await context.RespondWithTracking(request, response)
            .ThenMarkAsComplete<ReserveInventoryCommand>(cts.Token);

        eventBus.Verify(b => b.Respond(request, response, It.IsAny<Type>(), It.IsAny<Guid>(), cts.Token), Times.Once);
    }

    // 4: ScheduleWithTracking(...).ThenMarkAsComplete<TStep>(ct).
    [Fact]
    public async Task ScheduleWithTracking_ThenMarkAsComplete_Generic_Transitions_Explicit_Step()
    {
        var (context, _, _, scheduler) = CreateContext();
        using var cts = new CancellationTokenSource();
        scheduler.Setup(s => s.ScheduleAsync(It.IsAny<OrderCreatedEvent>(), It.IsAny<OrderCreatedEvent>(),
                It.IsAny<Type>(), It.IsAny<Guid>(), ScheduleDelay.FiveSeconds, null, cts.Token))
            .ReturnsAsync(Guid.NewGuid());

        await context.ScheduleWithTracking(new OrderCreatedEvent(), ScheduleDelay.FiveSeconds)
            .ThenMarkAsComplete<ReserveInventoryCommand>(cts.Token);

        scheduler.Verify(s => s.ScheduleAsync(It.IsAny<OrderCreatedEvent>(), It.IsAny<OrderCreatedEvent>(),
            It.IsAny<Type>(), It.IsAny<Guid>(), ScheduleDelay.FiveSeconds, null, cts.Token), Times.Once);
    }

    // 9 & 10: both the inferred non-generic form and the explicit generic form remain available on the
    // same ISagaStepFluent instance, and both resolve to the context's own current step (never the
    // outgoing message type), confirmed via the SagaStore step-type argument.
    [Fact]
    public async Task Generic_And_NonGeneric_ThenMarkAsComplete_Both_Log_The_Context_Current_Step()
    {
        var (genericContext, _, genericStore, _) = CreateContextWithStore();
        await genericContext.SendWithTracking(new ReserveInventoryCommand())
            .ThenMarkAsComplete<CreateOrderCommand>();

        var (inferredContext, _, inferredStore, _) = CreateContextWithStore();
        await inferredContext.SendWithTracking(new ReserveInventoryCommand())
            .ThenMarkAsComplete();

        // Verified via raw invocations (not Mock.Verify with a lambda) because Moq cannot reliably
        // disambiguate the two LogStepAsync overloads (Exception? vs SagaStepFailureInfo?) by expression.
        Assert.Equal(typeof(OrderCreatedEvent), Assert.Single(genericStore.Invocations).Arguments[3]);
        Assert.Equal(typeof(OrderCreatedEvent), Assert.Single(inferredStore.Invocations).Arguments[3]);
    }

    private sealed class OrderCreatedResponse : ResponseBase<CreateOrderCommand>;

    private sealed class ReserveInventoryCommand : CommandBase, ITestAppCommand;
}
