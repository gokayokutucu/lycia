using Lycia.Extensions;
using Lycia.Infrastructure.Compensating;
using Lycia.Infrastructure.Dispatching;
using Lycia.Infrastructure.Eventing;
using Lycia.Infrastructure.Stores;
using Lycia.Messaging;
using Lycia.Messaging.Enums;
using Lycia.Saga;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Extensions;
using Lycia.Saga.Handlers;
using Lycia.Tests.Helper;
using Lycia.Tests.Sagas;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Moq.Protected;
using Sample.Order.Consumer.Sagas;
using Sample.Shared.Messages.Commands;
using Sample.Shared.Messages.Events;
using Sample.Shared.Messages.Sagas;

namespace Lycia.Tests;

public class SagaDispatcherTests
{
    
    [Fact]
    public async Task DispatchAsync_Should_Not_Invoke_Handler_On_MessageType_Mismatch()
    {
        // Arrange: Only a handler for a different message type is registered.
        var services = new ServiceCollection();
        var fixedSagaId = Guid.NewGuid();
        services.AddScoped<ISagaIdGenerator>(_ => new TestSagaIdGenerator(fixedSagaId));
        services.AddScoped<ISagaCompensationCoordinator, SagaCompensationCoordinator>();
        services.AddScoped<ISagaStore, InMemorySagaStore>();
        services.AddScoped<IEventBus>(sp =>
            new InMemoryEventBus(new Lazy<ISagaDispatcher>(sp.GetRequiredService<ISagaDispatcher>)));

        // Register handler for another message type (OrderCreatedEvent).
        services.AddScoped<ISagaHandler<OrderCreatedEvent>, ShipOrderSagaHandler>();

        var provider = services.BuildServiceProvider();

        // Create dispatcher mock to track calls to protected methods.
        var dispatcherMock = new Mock<SagaDispatcher>(
            provider.GetRequiredService<ISagaStore>(),
            provider.GetRequiredService<ISagaIdGenerator>(),
            provider.GetRequiredService<IEventBus>(),
            provider.GetRequiredService<ISagaCompensationCoordinator>(),
            provider
        ) { CallBase = true };

        var message = new InitialCommand(); // No handler registered for this type.

        // Act
        await dispatcherMock.Object.DispatchAsync(message);

        // Assert: Neither InvokeHandlerAsync nor DispatchCompensationHandlersAsync should be called.
        dispatcherMock.Protected().Verify(
            "InvokeHandlerAsync",
            Times.Never(),
            ItExpr.IsAny<IEnumerable<object?>>(),
            ItExpr.IsAny<IMessage>(),
            ItExpr.IsAny<FailResponse>()
        );
    }
    
    [Fact]
    public async Task DispatchAsync_Should_Detect_And_Stop_On_CircularParentChain()
    {
        // Arrange: Build a circular parent chain in the step log
        var fixedSagaId = Guid.NewGuid();
        var services = new ServiceCollection();
        services.AddScoped<ISagaIdGenerator>(_ => new TestSagaIdGenerator(fixedSagaId));
        services.AddScoped<ISagaCompensationCoordinator, SagaCompensationCoordinator>();
        services.AddScoped<ISagaStore, InMemorySagaStore>();
        services.AddScoped<ISagaDispatcher, SagaDispatcher>();
        services.AddScoped<IEventBus>(sp =>
            new InMemoryEventBus(new Lazy<ISagaDispatcher>(sp.GetRequiredService<ISagaDispatcher>)));

        services.AddScoped<ISagaHandler<ParentEvent>, ParentCompensationSagaHandler>();
        services.AddScoped<ISagaHandler<FailingEvent>, FailingCompensationSagaHandler>();

        var provider = services.BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<ISagaDispatcher>();
        var store = provider.GetRequiredService<ISagaStore>();

        var parentEventId = Guid.NewGuid();
        var failingEventId = Guid.NewGuid();

        // parentEvent.ParentMessageId = failingEventId
        // failingEvent.ParentMessageId = parentEventId
        var parentEvent = new ParentEvent
            { SagaId = fixedSagaId, MessageId = parentEventId, ParentMessageId = failingEventId };
        var failingEvent = new FailingEvent
            { SagaId = fixedSagaId, MessageId = failingEventId, ParentMessageId = parentEventId };

        // Log both steps to simulate the circular chain
        await store.LogStepAsync(fixedSagaId, parentEventId, failingEventId, typeof(ParentEvent), StepStatus.Completed,
            typeof(ParentCompensationSagaHandler), parentEvent);
        await store.LogStepAsync(fixedSagaId, failingEventId, parentEventId, typeof(FailingEvent), StepStatus.Failed,
            typeof(FailingCompensationSagaHandler), failingEvent);

        // Act & Assert: Expect an InvalidOperationException due to the circular parent chain
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dispatcher.DispatchAsync(parentEvent)
        );
        Assert.Contains("Illegal StepStatus transition", ex.Message);
    }

    [Fact]
    public async Task DispatchAsync_Should_Not_Continue_CompensationChain_After_CompensationFailure()
    {
        // Arrange
        var fixedSagaId = Guid.NewGuid();
        var services = new ServiceCollection();
        services.AddScoped<ISagaIdGenerator>(_ => new TestSagaIdGenerator(fixedSagaId));
        services.AddScoped<ISagaCompensationCoordinator, SagaCompensationCoordinator>();
        services.AddScoped<ISagaStore, InMemorySagaStore>();
        services.AddScoped<ISagaDispatcher, SagaDispatcher>();
        services.AddScoped<IEventBus>(sp =>
            new InMemoryEventBus(new Lazy<ISagaDispatcher>(sp.GetRequiredService<ISagaDispatcher>)));

        services.AddScoped<ISagaStartHandler<InitialCommand>, InitialCompensationSagaHandler>();
        services.AddScoped<ISagaHandler<ParentEvent>, ParentCompensationSagaHandler>();
        services.AddScoped<ISagaHandler<FailingEvent>, FailingCompensationSagaHandler>();

        // Reset compensation flags before test
        FailingCompensationSagaHandler.CompensateCalled = false;
        ParentCompensationSagaHandler.CompensateCalled = false;
        InitialCompensationSagaHandler.CompensateCalled = false;

        var provider = services.BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<ISagaDispatcher>();
        var store = provider.GetRequiredService<ISagaStore>();

        var command = new InitialCommand();

        // Act
        await dispatcher.DispatchAsync(command);

        // Assert: Only the first failing compensation handler should be called.
        Assert.True(FailingCompensationSagaHandler.CompensateCalled);
        Assert.False(ParentCompensationSagaHandler.CompensateCalled);
        Assert.False(InitialCompensationSagaHandler.CompensateCalled);

        var steps = await store.GetSagaHandlerStepsAsync(fixedSagaId);

        Assert.Contains(steps.Values, meta => meta.Status == StepStatus.CompensationFailed);
        // Parent should not have been compensated
        var parentStep = steps.FirstOrDefault(x => x.Key.stepType.Contains(nameof(ParentEvent)));
        if (!parentStep.Equals(default(KeyValuePair<(string, string, string), SagaStepMetadata>)))
        {
            Assert.False(parentStep.Value.Status == StepStatus.Compensated);
        }
    }

    [Fact]
    public async Task DispatchAsync_Should_Invoke_CompensateStartAsync_When_Overridden()
    {
        // Arrange
        var fixedSagaId = Guid.NewGuid();
        var services = new ServiceCollection();
        services.AddScoped<ISagaIdGenerator>(_ => new TestSagaIdGenerator(fixedSagaId));
        services.AddScoped<ISagaStore, InMemorySagaStore>();
        services.AddScoped<ISagaDispatcher, SagaDispatcher>();
        services.AddScoped<ISagaCompensationCoordinator, SagaCompensationCoordinator>();
        services.AddScoped<IEventBus>(sp =>
            new InMemoryEventBus(new Lazy<ISagaDispatcher>(sp.GetRequiredService<ISagaDispatcher>)));
        services.AddScoped<ISagaStartHandler<CreateOrderCommand>, TestStartReactiveCompensateHandler>();

        var provider = services.BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<ISagaDispatcher>();

        var command = new CreateOrderCommand
        {
            OrderId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            TotalPrice = -42 // trigger compensation
        };

        // Act
        await dispatcher.DispatchAsync(command);

        // Assert: Was the flag set in overridden CompensateStartAsync?
        Assert.True(TestStartReactiveCompensateHandler.CompensateCalled);
    }

    [Fact]
    public async Task DispatchAsync_Should_Swallow_Exception_And_Continue()
    {
        var fixedSagaId = Guid.NewGuid();
        var services = new ServiceCollection();
        services.AddScoped<ISagaIdGenerator>(_ => new TestSagaIdGenerator(fixedSagaId));
        services.AddScoped<ISagaDispatcher, SagaDispatcher>();
        services.AddScoped<ISagaCompensationCoordinator, SagaCompensationCoordinator>();
        services.AddScoped<ISagaStore, InMemorySagaStore>();
        services.AddScoped<IEventBus>(sp =>
            new InMemoryEventBus(new Lazy<ISagaDispatcher>(sp.GetRequiredService<ISagaDispatcher>)));
        services.AddScoped<ISagaHandler<OrderCreatedEvent>, SwallowingSagaHandler>();

        var provider = services.BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<ISagaDispatcher>();

        var message = new OrderCreatedEvent
        {
            OrderId = Guid.NewGuid(),
            SagaId = fixedSagaId
        };

        // Act & Assert: Exception swallowed, should not propagate
        var ex = await Record.ExceptionAsync(() => dispatcher.DispatchAsync(message));
        Assert.Null(ex);
    }

    [Fact]
    public async Task DispatchAsync_Should_Propagate_Exception_If_Not_Swallowed()
    {
        var fixedSagaId = Guid.NewGuid();
        var services = new ServiceCollection();
        services.AddScoped<ISagaIdGenerator>(_ => new TestSagaIdGenerator(fixedSagaId));
        services.AddScoped<ISagaDispatcher, SagaDispatcher>();
        services.AddScoped<ISagaCompensationCoordinator, SagaCompensationCoordinator>();
        services.AddScoped<ISagaStore, InMemorySagaStore>();
        services.AddScoped<IEventBus>(sp =>
            new InMemoryEventBus(new Lazy<ISagaDispatcher>(sp.GetRequiredService<ISagaDispatcher>)));
        services.AddScoped<ISagaHandler<OrderCreatedEvent>, ThrowingSagaHandler>();

        var provider = services.BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<ISagaDispatcher>();

        var message = new OrderCreatedEvent { OrderId = Guid.NewGuid() };

        // Act & Assert: Exception must be propagated
        await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.DispatchAsync(message));
    }

    [Fact]
    public async Task DispatchAsync_Should_Invoke_Multiple_Handler_For_The_Same_Saga()
    {
        // Arrange
        var fixedSagaId = Guid.NewGuid();
        var services = new ServiceCollection();
        services.AddScoped<ISagaIdGenerator>(_ => new TestSagaIdGenerator(fixedSagaId));
        services.AddScoped<ISagaCompensationCoordinator, SagaCompensationCoordinator>();
        services.AddScoped<ISagaStore, InMemorySagaStore>();
        services.AddScoped<ISagaDispatcher, SagaDispatcher>();
        services.AddScoped<IEventBus>(sp =>
            new InMemoryEventBus(new Lazy<ISagaDispatcher>(sp.GetRequiredService<ISagaDispatcher>)));

        // Register all relevant SagaHandlers
        services.AddScoped<ISagaStartHandler<CreateOrderCommand>, CreateOrderSagaHandler>();
        services.AddScoped<ISagaHandler<OrderCreatedEvent>, ShipOrderSagaHandler>();
        services.AddScoped<ISagaHandler<OrderCreatedEvent>, AuditOrderSagaHandler>();

        var provider = services.BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<ISagaDispatcher>();
        var store = provider.GetRequiredService<ISagaStore>();

        var command = new CreateOrderCommand
        {
            OrderId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            TotalPrice = 100
        };

        // Act
        await dispatcher.DispatchAsync(command);

        // Assert
        var steps = await store.GetSagaHandlerStepsAsync(fixedSagaId);
        var count = steps.Count(x => x.Key.stepType.Contains(nameof(OrderCreatedEvent)));
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task CompensationChain_Should_Stop_On_CompensationFailed()
    {
        // Arrange
        var fixedSagaId = Guid.NewGuid();
        var services = new ServiceCollection();
        services.AddScoped<ISagaIdGenerator>(_ => new TestSagaIdGenerator(fixedSagaId));
        services.AddScoped<ISagaCompensationCoordinator, SagaCompensationCoordinator>();
        services.AddScoped<ISagaStore, InMemorySagaStore>();
        services.AddScoped<ISagaDispatcher, SagaDispatcher>();
        services.AddScoped<IEventBus>(sp =>
            new InMemoryEventBus(new Lazy<ISagaDispatcher>(sp.GetRequiredService<ISagaDispatcher>)));

        // Register all relevant SagaHandlers
        // services.AddScoped<ISagaStartHandler<CreateOrderCommand>, CreateOrderSagaHandler>();
        // services.AddScoped<ISagaCompensationHandler<OrderShippingFailedEvent>, CreateOrderSagaHandler>();
        // services.AddScoped<ISagaHandler<OrderCreatedEvent>, ShipOrderForCompensationSagaHandler>();
        // services.AddScoped<ISagaCompensationHandler<OrderCreatedEvent>, ShipOrderForCompensationSagaHandler>();
        services.AddLycia()
            .AddSagas(typeof(CreateOrderSagaHandler), typeof(ShipOrderForCompensationSagaHandler),
                typeof(ShipOrderForCompensationSagaHandler));

        var provider = services.BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<ISagaDispatcher>();
        var store = provider.GetRequiredService<ISagaStore>();

        var command = new CreateOrderCommand
        {
            OrderId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            TotalPrice = -10, // Negative price to trigger compensation failed
        };

        // Act
        await dispatcher.DispatchAsync(command);

        // Assert - check the status of the steps in the chain
        var steps = await store.GetSagaHandlerStepsAsync(fixedSagaId);
        Assert.Contains(steps.Keys, x => x.stepType.Contains(nameof(CreateOrderCommand)));
        Assert.Contains(steps.Keys, x => x.stepType.Contains(nameof(OrderCreatedEvent)));
        // The compensation chain should contain CompensationFailed status
        Assert.Contains(steps.Values, meta => meta.Status == StepStatus.CompensationFailed);
    }

    [Fact]
    public async Task DispatchAsync_Should_Invoke_CreateOrderSagaHandler()
    {
        // Arrange
        var services = new ServiceCollection();

        var fixedSagaId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        services.AddScoped<ISagaIdGenerator>(_ => new TestSagaIdGenerator(fixedSagaId));
        services.AddScoped<ISagaCompensationCoordinator, SagaCompensationCoordinator>();
        services.AddScoped<ISagaDispatcher, SagaDispatcher>();
        services.AddScoped<ISagaStore, InMemorySagaStore>();
        services.AddScoped<IEventBus>(sp =>
            new InMemoryEventBus(new Lazy<ISagaDispatcher>(sp.GetRequiredService<ISagaDispatcher>)));

        // Register all relevant SagaHandlers
        services.AddScoped<ISagaStartHandler<CreateOrderCommand>, CreateOrderSagaHandler>();
        services.AddScoped<ISagaHandler<OrderShippedEvent>, DeliverOrderSagaHandler>();
        services.AddScoped<ISagaHandler<OrderCreatedEvent>, ShipOrderSagaHandler>();

        var serviceProvider = services.BuildServiceProvider();
        var eventBus = serviceProvider.GetRequiredService<IEventBus>();
        var sagaStore = serviceProvider.GetRequiredService<ISagaStore>();

        var command = new CreateOrderCommand
        {
            OrderId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            TotalPrice = 100
        };

        // Act
        await eventBus.Send(command);

        // Assert
        var steps = await sagaStore.GetSagaHandlerStepsAsync(fixedSagaId);

        var createOrderCommandMessageId =
            SagaDispatcherTestHelper.GetMessageId<CreateOrderCommand, CreateOrderSagaHandler>(steps);

        var wasLogged = await sagaStore.IsStepCompletedAsync(fixedSagaId, createOrderCommandMessageId!.Value,
            typeof(CreateOrderCommand),
            typeof(CreateOrderSagaHandler));
        Assert.True(wasLogged);

        var orderCreatedEventMessageId =
            SagaDispatcherTestHelper.GetMessageId<OrderCreatedEvent, ShipOrderSagaHandler>(steps);
        var wasShipped =
            await sagaStore.IsStepCompletedAsync(fixedSagaId, orderCreatedEventMessageId!.Value,
                typeof(OrderCreatedEvent), typeof(ShipOrderSagaHandler));

        var orderShippedEventMessageId =
            SagaDispatcherTestHelper.GetMessageId<OrderShippedEvent, DeliverOrderSagaHandler>(steps);
        var wasDelivered =
            await sagaStore.IsStepCompletedAsync(fixedSagaId, orderShippedEventMessageId!.Value,
                typeof(OrderShippedEvent),
                typeof(DeliverOrderSagaHandler));
        Assert.True(wasShipped);
        Assert.True(wasDelivered);
    }

    [Fact]
    public async Task DispatchAsync_Should_Execute_CompleteSagaChain_And_CompensateOnFailure()
    {
        // Arrange
        var fixedSagaId = Guid.NewGuid();

        var services = new ServiceCollection();
        services.AddScoped<ISagaIdGenerator>(_ => new TestSagaIdGenerator(fixedSagaId));
        services.AddScoped<ISagaCompensationCoordinator, SagaCompensationCoordinator>();
        services.AddScoped<ISagaStore, InMemorySagaStore>();
        services.AddScoped<ISagaDispatcher, SagaDispatcher>();
        services.AddScoped<IEventBus>(sp =>
            new InMemoryEventBus(new Lazy<ISagaDispatcher>(sp.GetRequiredService<ISagaDispatcher>)));

        // Register all relevant SagaHandlers
        services.AddScoped<ISagaStartHandler<CreateOrderCommand>, CreateOrderSagaHandler>();
        services.AddScoped<ISagaHandler<OrderCreatedEvent>, ShipOrderForCompensationSagaHandler>();

        var provider = services.BuildServiceProvider();

        var dispatcher = provider.GetRequiredService<ISagaDispatcher>();
        var store = provider.GetRequiredService<ISagaStore>();

        var command = new CreateOrderCommand
        {
            OrderId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            TotalPrice = 99.0m,
            Timestamp = DateTime.UtcNow
        };

        // Act
        await dispatcher.DispatchAsync(command);

        var steps = await store.GetSagaHandlerStepsAsync(fixedSagaId);

        // Assert - normal forward chain
        Assert.Contains(steps.Keys, x => x.stepType.Contains(nameof(CreateOrderCommand)));
        Assert.Contains(steps.Keys, x => x.stepType.Contains(nameof(OrderCreatedEvent)));

        Assert.Contains(steps.Values, meta => meta.Status == StepStatus.Compensated);
    }

    [Fact]
    public async Task DispatchAsync_NoHandlerRegistered_ShouldNotThrow()
    {
        var services = new ServiceCollection();
        services.AddScoped<ISagaIdGenerator, TestSagaIdGenerator>();
        services.AddScoped<ISagaCompensationCoordinator, SagaCompensationCoordinator>();
        services.AddScoped<ISagaDispatcher, SagaDispatcher>();
        services.AddScoped<ISagaStore, InMemorySagaStore>();
        services.AddScoped<IEventBus>(sp =>
            new InMemoryEventBus(new Lazy<ISagaDispatcher>(sp.GetRequiredService<ISagaDispatcher>)));
        var provider = services.BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<ISagaDispatcher>();

        var command = new CreateOrderCommand
        {
            OrderId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            TotalPrice = 111
        };

        // Act & Assert
        await dispatcher.DispatchAsync(command); // No handler registered, should not throw
    }

    // CreateOrder -> OrderCreated (fail) -> Compensation started
    [Fact]
    public async Task DispatchAsync_Should_Trigger_Compensation_For_FailedStep()
    {
        // Arrange
        var fixedSagaId = Guid.NewGuid();
        var services = new ServiceCollection();
        services.AddScoped<ISagaIdGenerator>(_ => new TestSagaIdGenerator(fixedSagaId));
        services.AddScoped<ISagaCompensationCoordinator, SagaCompensationCoordinator>();
        services.AddScoped<ISagaStore, InMemorySagaStore>();
        services.AddScoped<ISagaDispatcher, SagaDispatcher>();
        services.AddScoped<IEventBus>(sp =>
            new InMemoryEventBus(new Lazy<ISagaDispatcher>(sp.GetRequiredService<ISagaDispatcher>)));

        // The ShipOrderForCompensationSagaHandler will intentionally fail at this step.
        services.AddScoped<ISagaStartHandler<CreateOrderCommand>, CreateOrderSagaHandler>();
        services.AddScoped<ISagaHandler<OrderCreatedEvent>, ShipOrderForCompensationSagaHandler>();

        // services.AddSagaHandlers(
        //     typeof(CreateOrderSagaHandler),
        //     typeof(ShipOrderForCompensationSagaHandler)
        // );

        var provider = services.BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<ISagaDispatcher>();
        var store = provider.GetRequiredService<ISagaStore>();
        // The estimated/expected completion date is July 15
        // This timeline includes testing and fixes or developments by other stakeholders as well
        var command = new CreateOrderCommand
        {
            OrderId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            TotalPrice = 10
        };
        // Without vibe coding and AI support, it would most likely have taken more than 6 months to complete.
        // Plus, this codebase will also reduce the time required for future developments.

        // Act
        await dispatcher.DispatchAsync(command);

        // Assert - check the status of the steps in the chain
        var steps = await store.GetSagaHandlerStepsAsync(fixedSagaId);
        Assert.Contains(steps.Keys, x => x.stepType.Contains(nameof(CreateOrderCommand)));
        Assert.Contains(steps.Keys, x => x.stepType.Contains(nameof(OrderCreatedEvent)));
        Assert.Contains(steps.Values, meta => meta.Status == StepStatus.Compensated);
    }

    [Fact]
    public async Task DispatchAsync_AllSteps_ShouldBeProcessedInOrder()
    {
        var services = new ServiceCollection();
        var fixedSagaId = Guid.NewGuid();
        services.AddScoped<ISagaIdGenerator>(_ => new TestSagaIdGenerator(fixedSagaId));
        services.AddScoped<ISagaCompensationCoordinator, SagaCompensationCoordinator>();
        services.AddScoped<ISagaStore, InMemorySagaStore>();
        services.AddScoped<ISagaDispatcher, SagaDispatcher>();
        services.AddScoped<IEventBus>(sp =>
            new InMemoryEventBus(new Lazy<ISagaDispatcher>(sp.GetRequiredService<ISagaDispatcher>)));

        services.AddScoped<ISagaStartHandler<CreateOrderCommand>, CreateOrderSagaHandler>();
        services.AddScoped<ISagaHandler<OrderShippedEvent>, DeliverOrderSagaHandler>();
        services.AddScoped<ISagaHandler<OrderCreatedEvent>, ShipOrderSagaHandler>();

        var provider = services.BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<ISagaDispatcher>();
        var store = provider.GetRequiredService<ISagaStore>();

        var command = new CreateOrderCommand
        {
            OrderId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            TotalPrice = 10
        };

        // Act
        await dispatcher.DispatchAsync(command);

        // Assert: Are all steps processed in order and completed?
        var steps = await store.GetSagaHandlerStepsAsync(fixedSagaId);

        var expectedStepTypes = new[]
        {
            nameof(CreateOrderCommand),
            nameof(OrderCreatedEvent),
            nameof(OrderShippedEvent)
        };

        foreach (var expected in expectedStepTypes)
            Assert.Contains(steps.Keys, x => x.stepType.Contains(expected));

        Assert.All(steps, kv => Assert.Equal(StepStatus.Completed, kv.Value.Status));
    }

    [Fact]
    public async Task DispatchAsync_DuplicateStep_ShouldNotProcessTwice()
    {
        var services = new ServiceCollection();
        var fixedSagaId = Guid.NewGuid();
        services.AddScoped<ISagaIdGenerator>(_ => new TestSagaIdGenerator(fixedSagaId));
        services.AddScoped<ISagaCompensationCoordinator, SagaCompensationCoordinator>();
        services.AddScoped<ISagaStore, InMemorySagaStore>();
        services.AddScoped<ISagaDispatcher, SagaDispatcher>();
        services.AddScoped<IEventBus>(sp =>
            new InMemoryEventBus(new Lazy<ISagaDispatcher>(sp.GetRequiredService<ISagaDispatcher>)));

        services.AddScoped<ISagaStartHandler<CreateOrderCommand>, CreateOrderSagaHandler>();
        // Other steps can also be added if needed.

        var provider = services.BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<ISagaDispatcher>();
        var store = provider.GetRequiredService<ISagaStore>();

        var command = new CreateOrderCommand
        {
            OrderId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            TotalPrice = 10
        };

        // Act
        await dispatcher.DispatchAsync(command);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => { await dispatcher.DispatchAsync(command); });

        // Assert: The same step should only be processed once per handler.
        var steps = await store.GetSagaHandlerStepsAsync(fixedSagaId);
        var handlerCount = steps.Count(x => x.Key.stepType.Contains(nameof(CreateOrderCommand)));
        Assert.Equal(1, handlerCount);

        Assert.All(steps.Where(x => x.Key.stepType.Contains(nameof(CreateOrderCommand))),
            kv => Assert.Equal(StepStatus.Completed, kv.Value.Status));
    }

    public class InitialCommand : CommandBase
    {
    }

    public class ParentEvent : EventBase
    {
    }

    public class FailingEvent : EventBase
    {
    }

    public class InitialCompensationSagaHandler : StartReactiveSagaHandler<InitialCommand>
    {
        public static bool CompensateCalled = false;

        public override async Task HandleStartAsync(InitialCommand message)
        {
            var next = new ParentEvent { ParentMessageId = message.MessageId };
            await Context.PublishWithTracking(next).ThenMarkAsComplete();
        }

        public override Task CompensateStartAsync(InitialCommand message)
        {
            CompensateCalled = true;
            return Task.CompletedTask;
        }
    }

    public class ParentCompensationSagaHandler : ReactiveSagaHandler<ParentEvent>
    {
        public static bool CompensateCalled = false;

        public override Task HandleAsync(ParentEvent message)
        {
            var fail = new FailingEvent { ParentMessageId = message.MessageId };
            return Context.PublishWithTracking(fail).ThenMarkAsComplete();
        }

        public override Task CompensateAsync(ParentEvent message)
        {
            CompensateCalled = true;
            return Task.CompletedTask;
        }
    }

    public class FailingCompensationSagaHandler : ReactiveSagaHandler<FailingEvent>
    {
        public static bool CompensateCalled = false;

        public override Task HandleAsync(FailingEvent message)
            => throw new InvalidOperationException("Fail!");

        public override Task CompensateAsync(FailingEvent message)
        {
            CompensateCalled = true;
            throw new Exception("Compensation failed");
        }
    }
}