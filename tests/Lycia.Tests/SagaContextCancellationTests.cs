// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Scheduling;
using Lycia.Saga.Contexts;
using Lycia.Saga.Messaging;
using Lycia.Tests.Messages;
using Moq;

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

    // 16. Source compatibility: the older WithTracking(message, cancellationToken).Then...() shape still
    // works, and its token still reaches the deferred Send via fallback when the terminal token is default.
    [Fact]
    public async Task Legacy_WithTracking_Token_Still_Reaches_Send_Via_Fallback()
    {
        var (context, eventBus, _, _) = CreateContext();
        using var cts = new CancellationTokenSource();

        await context.SendWithTracking(new CreateOrderCommand(), cts.Token).ThenMarkAsComplete();

        eventBus.Verify(b => b.Send(It.IsAny<CreateOrderCommand>(), It.IsAny<Type>(), It.IsAny<Guid>(), cts.Token), Times.Once);
    }

    private sealed class OrderCreatedResponse : ResponseBase<CreateOrderCommand>;
}
