using Lycia.Extensions;
using Lycia.Extensions.Serialization;
using Lycia.Infrastructure.Compensating;
using Lycia.Infrastructure.Dispatching;
using Lycia.Infrastructure.Eventing;
using Lycia.Infrastructure.Stores;
using Lycia.Messaging;
using Lycia.Messaging.Enums;
using Lycia.Saga;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Exceptions;
using Lycia.Saga.Extensions;
using Lycia.Saga.Handlers;
using Lycia.Saga.Handlers.Abstractions;
using Lycia.Tests.Helpers;
using Lycia.Tests.Messages;
using Lycia.Tests.Sagas;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;

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
        services.AddScoped<IMessageSerializer, NewtonsoftJsonMessageSerializer>();
        services.AddScoped<IEventBus>(sp =>
            new InMemoryEventBus(new Lazy<ISagaDispatcher>(sp.GetRequiredService<ISagaDispatcher>)));

        // Register handler for another message type (OrderCreatedEvent).
        services.AddScoped<ShipOrderSagaHandler>();

        var provider = services.BuildServiceProvider();

        // Create dispatcher mock to track calls to protected methods.
        var dispatcherMock = new Mock<SagaDispatcher>(
            provider.GetRequiredService<ISagaStore>(),
            provider.GetRequiredService<ISagaIdGenerator>(),
            provider
        ) { CallBase = true };

        var message = new InitialCommand(); // No handler registered for this type.

        // Act & Assert: Expect no handler to be invoked
        await Assert.ThrowsAsync<InvalidOperationException>(async () => 
            await dispatcherMock.Object.DispatchAsync(message, handlerType: typeof(ShipOrderForCompensationSagaHandler), sagaId: fixedSagaId, CancellationToken.None));
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
        services.AddScoped<IMessageSerializer, NewtonsoftJsonMessageSerializer>();
        services.AddScoped<IEventBus>(sp =>
            new InMemoryEventBus(new Lazy<ISagaDispatcher>(sp.GetRequiredService<ISagaDispatcher>)));

        services.AddScoped<ISagaHandler<ParentEvent>, ParentCompensationSagaHandler>();
        services.AddScoped<ISagaHandler<FailingEvent>, FailingCompensationSagaHandler>();

        var provider = services.BuildServiceProvider();
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
            typeof(ParentCompensationSagaHandler), parentEvent, (SagaStepFailureInfo?)null);


        // Act & Assert: Expect an InvalidOperationException due to the circular parent chain
        await Assert.ThrowsAsync<SagaStepCircularChainException>(() =>
            store.LogStepAsync(fixedSagaId, failingEventId, parentEventId, typeof(FailingEvent), StepStatus.Failed,
                typeof(FailingCompensationSagaHandler), failingEvent, (SagaStepFailureInfo?)null)
        );
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
        services.AddScoped<IMessageSerializer, NewtonsoftJsonMessageSerializer>();
        services.AddScoped<IEventBus>(sp =>
            new InMemoryEventBus(new Lazy<ISagaDispatcher>(sp.GetRequiredService<ISagaDispatcher>)));
        services.AddScoped<SwallowingSagaHandler>();

        var provider = services.BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<ISagaDispatcher>();

        var message = new OrderCreatedEvent
        {
            OrderId = Guid.NewGuid(),
            SagaId = fixedSagaId
        };

        // Act & Assert: Exception swallowed, should not propagate
        var ex = await Record.ExceptionAsync(() => dispatcher.DispatchAsync(message, handlerType: typeof(SwallowingSagaHandler), sagaId: fixedSagaId, CancellationToken.None));
        Assert.Null(ex);
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
        services.AddScoped<IMessageSerializer, NewtonsoftJsonMessageSerializer>();
        services.AddScoped<ISagaDispatcher, SagaDispatcher>();
        services.AddScoped<IEventBus>(sp =>
            new InMemoryEventBus(new Lazy<ISagaDispatcher>(sp.GetRequiredService<ISagaDispatcher>)));

        // Register all relevant SagaHandlers
        services.AddScoped<CreateOrderSagaHandler>();
        services.AddScoped<ShipOrderSagaHandler>();
        services.AddScoped<AuditOrderSagaHandler>();

        var provider = services.BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<ISagaDispatcher>();
        var store = provider.GetRequiredService<ISagaStore>();

        var command = new CreateOrderCommand
        {
            OrderId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            TotalPrice = 100
        };

        var orderCreatedEvent = new OrderCreatedEvent()
        {
            OrderId = command.OrderId,
            UserId = command.UserId,
            TotalPrice = command.TotalPrice,
            SagaId = fixedSagaId,
            ParentMessageId = command.MessageId
        };

        // Act
        await dispatcher.DispatchAsync(command, handlerType: typeof(CreateOrderSagaHandler), sagaId: fixedSagaId, CancellationToken.None);
        await dispatcher.DispatchAsync(orderCreatedEvent, handlerType: typeof(ShipOrderSagaHandler), sagaId: fixedSagaId, CancellationToken.None);
        await dispatcher.DispatchAsync(orderCreatedEvent, handlerType: typeof(AuditOrderSagaHandler), sagaId: fixedSagaId, CancellationToken.None);

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

        // Register all relevant SagaHandlers
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["ApplicationId"] = "TestApp"
            }!)
            .Build();

        services.AddLyciaInMemory(configuration)
            .AddSagas(typeof(CreateOrderSagaHandler), typeof(ShipOrderForCompensationSagaHandler))
            .Build();
        
        services.AddScoped<ISagaIdGenerator>(_ => new TestSagaIdGenerator(fixedSagaId));

        var provider = services.BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<ISagaDispatcher>();
        var store = provider.GetRequiredService<ISagaStore>();
        
        var startMessageId = Guid.NewGuid();

        var command = new OrderCreatedEvent()
        {
            MessageId = Guid.NewGuid(),
            ParentMessageId = startMessageId,
            OrderId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            TotalPrice = -10, // Negative price to trigger compensation failed
        };
        
        await store.LogStepAsync(fixedSagaId, startMessageId, Guid.Empty, typeof(CreateOrderCommand), StepStatus.Completed,
            typeof(CreateOrderSagaHandler), command, (SagaStepFailureInfo?)null);

        // Act
        await dispatcher.DispatchAsync(command, handlerType: typeof(ShipOrderForCompensationSagaHandler), sagaId: fixedSagaId, CancellationToken.None);

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
        services.AddScoped<IMessageSerializer, NewtonsoftJsonMessageSerializer>();
        services.AddScoped<IEventBus>(sp =>
            new InMemoryEventBus(new Lazy<ISagaDispatcher>(sp.GetRequiredService<ISagaDispatcher>)));

        // Register all relevant SagaHandlers
        services.AddScoped<CreateOrderSagaHandler>();
        services.AddScoped<ShipOrderSagaHandler>();
        services.AddScoped<DeliverOrderSagaHandler>();

        var serviceProvider = services.BuildServiceProvider();
        var sagaStore = serviceProvider.GetRequiredService<ISagaStore>();
        var dispatcher = serviceProvider.GetRequiredService<ISagaDispatcher>();

        var command = new CreateOrderCommand
        {
            OrderId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            TotalPrice = 100
        };
        
        var orderCreatedEvent = new OrderCreatedEvent
        {
            OrderId = command.OrderId,
            UserId = command.UserId,
            TotalPrice = command.TotalPrice,
            SagaId = fixedSagaId,
            ParentMessageId = command.MessageId
        };
        
        var orderShippedEvent = new OrderShippedEvent
        {
            OrderId = command.OrderId,
            SagaId = fixedSagaId,
            ParentMessageId = orderCreatedEvent.MessageId
        };

        // Act-1
        await dispatcher.DispatchAsync(command, handlerType: typeof(CreateOrderSagaHandler), sagaId: fixedSagaId, CancellationToken.None);

        // Assert
        var steps = await sagaStore.GetSagaHandlerStepsAsync(fixedSagaId);

        var createOrderCommandMessageId =
            SagaTestHelper.GetMessageId<CreateOrderCommand, CreateOrderSagaHandler>(steps);

        var wasLogged = await sagaStore.IsStepCompletedAsync(fixedSagaId, createOrderCommandMessageId!.Value,
            typeof(CreateOrderCommand),
            typeof(CreateOrderSagaHandler));
        Assert.True(wasLogged);

        // Act-2
        await dispatcher.DispatchAsync(orderCreatedEvent, handlerType: typeof(ShipOrderSagaHandler), sagaId: fixedSagaId, CancellationToken.None);
        
        // Assert
        steps = await sagaStore.GetSagaHandlerStepsAsync(fixedSagaId);
        var orderCreatedEventMessageId =
            SagaTestHelper.GetMessageId<OrderCreatedEvent, ShipOrderSagaHandler>(steps);
        var wasShipped =
            await sagaStore.IsStepCompletedAsync(fixedSagaId, orderCreatedEventMessageId!.Value,
                typeof(OrderCreatedEvent), typeof(ShipOrderSagaHandler));

        // Act-3
        await dispatcher.DispatchAsync(orderShippedEvent, handlerType: typeof(DeliverOrderSagaHandler), sagaId: fixedSagaId, CancellationToken.None);
        
        // Assert
        steps = await sagaStore.GetSagaHandlerStepsAsync(fixedSagaId);
        var orderShippedEventMessageId =
            SagaTestHelper.GetMessageId<OrderShippedEvent, DeliverOrderSagaHandler>(steps);
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
        services.AddScoped<IMessageSerializer, NewtonsoftJsonMessageSerializer>();
        services.AddScoped<ISagaDispatcher, SagaDispatcher>();
        services.AddScoped<IEventBus>(sp =>
            new InMemoryEventBus(new Lazy<ISagaDispatcher>(sp.GetRequiredService<ISagaDispatcher>)));

        // Register all relevant SagaHandlers
        services.AddScoped<CreateOrderSagaHandler>();
        services.AddScoped<ShipOrderForCompensationSagaHandler>();

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
        
        var orderCreatedEvent = new OrderCreatedEvent
        {
            OrderId = command.OrderId,
            UserId = command.UserId,
            TotalPrice = command.TotalPrice,
            SagaId = fixedSagaId,
            ParentMessageId = command.MessageId
        };

        // Act
        await dispatcher.DispatchAsync(command, handlerType: typeof(CreateOrderSagaHandler), sagaId: fixedSagaId, CancellationToken.None);
        await dispatcher.DispatchAsync(orderCreatedEvent, handlerType: typeof(ShipOrderForCompensationSagaHandler), sagaId: fixedSagaId, CancellationToken.None);

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
        services.AddScoped<IMessageSerializer, NewtonsoftJsonMessageSerializer>();
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
        await dispatcher.DispatchAsync(command, handlerType: null, sagaId: null, CancellationToken.None); // No handler registered, should not throw
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
        services.AddScoped<IMessageSerializer, NewtonsoftJsonMessageSerializer>();
        services.AddScoped<IEventBus>(sp =>
            new InMemoryEventBus(new Lazy<ISagaDispatcher>(sp.GetRequiredService<ISagaDispatcher>)));

        // The ShipOrderForCompensationSagaHandler will intentionally fail at this step.
        services.AddScoped<CreateOrderSagaHandler>();
        services.AddScoped<ShipOrderForCompensationSagaHandler>();
        

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
        var orderCreatedEvent = new OrderCreatedEvent
        {
            OrderId = command.OrderId,
            UserId = command.UserId,
            TotalPrice = command.TotalPrice,
            SagaId = fixedSagaId,
            ParentMessageId = command.MessageId
        };
        // Without vibe coding and AI support, it would most likely have taken more than 6 months to complete.
        // Plus, this codebase will also reduce the time required for future developments.

        // Act
        await dispatcher.DispatchAsync(command, handlerType: typeof(CreateOrderSagaHandler), sagaId: fixedSagaId, CancellationToken.None);
        await dispatcher.DispatchAsync(orderCreatedEvent, handlerType: typeof(ShipOrderForCompensationSagaHandler), sagaId: fixedSagaId, CancellationToken.None);
        

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
        services.AddScoped<IMessageSerializer, NewtonsoftJsonMessageSerializer>();
        services.AddScoped<ISagaDispatcher, SagaDispatcher>();
        services.AddScoped<IEventBus>(sp =>
            new InMemoryEventBus(new Lazy<ISagaDispatcher>(sp.GetRequiredService<ISagaDispatcher>)));

        services.AddScoped<CreateOrderSagaHandler>();
        services.AddScoped<DeliverOrderSagaHandler>();
        services.AddScoped<ShipOrderSagaHandler>();

        var provider = services.BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<ISagaDispatcher>();
        var store = provider.GetRequiredService<ISagaStore>();

        var command = new CreateOrderCommand
        {
            OrderId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            TotalPrice = 10
        };
        
        var orderCreatedEvent = new OrderCreatedEvent
        {
            OrderId = command.OrderId,
            UserId = command.UserId,
            TotalPrice = command.TotalPrice,
            SagaId = fixedSagaId,
            ParentMessageId = command.MessageId
        };
        
        var orderShippedEvent = new OrderShippedEvent
        {
            OrderId = command.OrderId,
            SagaId = fixedSagaId,
            ParentMessageId = orderCreatedEvent.MessageId
        };

        // Act
        await dispatcher.DispatchAsync(command, handlerType: typeof(CreateOrderSagaHandler), sagaId: fixedSagaId, CancellationToken.None);
        await dispatcher.DispatchAsync(orderCreatedEvent, handlerType: typeof(ShipOrderSagaHandler), sagaId: fixedSagaId, CancellationToken.None);
        await dispatcher.DispatchAsync(orderShippedEvent, handlerType: typeof(DeliverOrderSagaHandler), sagaId: fixedSagaId, CancellationToken.None);

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
        services.AddScoped<IMessageSerializer, NewtonsoftJsonMessageSerializer>();
        services.AddScoped<IEventBus>(sp =>
            new InMemoryEventBus(new Lazy<ISagaDispatcher>(sp.GetRequiredService<ISagaDispatcher>)));

        services.AddScoped<CreateOrderSagaHandler>();
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
        await dispatcher.DispatchAsync(command, handlerType: typeof(CreateOrderSagaHandler), sagaId: fixedSagaId, CancellationToken.None);

        //await Assert.ThrowsAsync<InvalidOperationException>(async () => { await dispatcher.DispatchAsync(command); });

        try
        {
            await dispatcher.DispatchAsync(command, handlerType: typeof(CreateOrderSagaHandler), sagaId: fixedSagaId, CancellationToken.None);
        }
        catch (Exception e)
        {
            throw;
        }
        
        // Assert: The same step should only be processed once per handler.
        var steps = await store.GetSagaHandlerStepsAsync(fixedSagaId);
        var handlerCount = steps.Count(x => x.Key.stepType.Contains(nameof(CreateOrderCommand)));
        Assert.Equal(1, handlerCount);

        Assert.All(steps.Where(x => x.Key.stepType.Contains(nameof(CreateOrderCommand))),
            kv => Assert.Equal(StepStatus.Completed, kv.Value.Status));
    }
    
    [Fact]
    public async Task DispatchAsync_Should_Invoke_CompensateStartAsync_When_Overridden()
    {
        // Arrange
        var fixedSagaId = Guid.NewGuid();
        var services = new ServiceCollection();
        services.AddScoped<ISagaIdGenerator>(_ => new TestSagaIdGenerator(fixedSagaId));
        services.AddScoped<ISagaStore, InMemorySagaStore>();
        services.AddScoped<IMessageSerializer, NewtonsoftJsonMessageSerializer>();
        services.AddScoped<ISagaDispatcher, SagaDispatcher>();
        services.AddScoped<ISagaCompensationCoordinator, SagaCompensationCoordinator>();
        services.AddScoped<IEventBus>(sp =>
            new InMemoryEventBus(new Lazy<ISagaDispatcher>(sp.GetRequiredService<ISagaDispatcher>)));
        services.AddScoped<TestStartReactiveCompensateHandler>();

        var provider = services.BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<ISagaDispatcher>();

        var command = new CreateOrderCommand
        {
            OrderId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            TotalPrice = -42 // trigger compensation
        };

        // Act
        await dispatcher.DispatchAsync(command, handlerType: typeof(TestStartReactiveCompensateHandler), sagaId: fixedSagaId, CancellationToken.None);

        // Assert: Was the flag set in overridden CompensateStartAsync?
        Assert.True(TestStartReactiveCompensateHandler.CompensateCalled);
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

        public override async Task HandleStartAsync(InitialCommand message, CancellationToken cancellationToken = default)
        {
            var next = new ParentEvent { ParentMessageId = message.MessageId };
            await Context.PublishWithTracking(next).ThenMarkAsComplete();
        }

        public override Task CompensateStartAsync(InitialCommand message, CancellationToken cancellationToken = default)
        {
            CompensateCalled = true;
            return Task.CompletedTask;
        }
    }

    public class ParentCompensationSagaHandler : ReactiveSagaHandler<ParentEvent>
    {
        public static bool CompensateCalled = false;

        public override Task HandleAsync(ParentEvent message, CancellationToken cancellationToken = default)
        {
            var fail = new FailingEvent { ParentMessageId = message.MessageId };
            return Context.PublishWithTracking(fail).ThenMarkAsComplete();
        }

        public override Task CompensateAsync(ParentEvent message, CancellationToken cancellationToken = default)
        {
            CompensateCalled = true;
            return Task.CompletedTask;
        }
    }

    public class FailingCompensationSagaHandler : ReactiveSagaHandler<FailingEvent>
    {
        public static bool CompensateCalled = false;

        public override Task HandleAsync(FailingEvent message, CancellationToken cancellationToken = default)
        {
            CompensateCalled = true;
            // Simulate a failure in the handler
            // Uncomment the next line to simulate a failure and trigger compensation
            // throw new Exception("Simulated failure in FailingCompensationSagaHandler");
            Context.MarkAsComplete<FailingEvent>();
            return Task.CompletedTask;
        }
            //=> throw new InvalidOperationException("Fail!");

        public override Task CompensateAsync(FailingEvent message, CancellationToken cancellationToken = default)
        {
            CompensateCalled = true;
            throw new Exception("Compensation failed");
        }
    }
}