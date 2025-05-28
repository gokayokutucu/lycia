using Lycia.Infrastructure.Abstractions;
using Lycia.Infrastructure.Dispatching;
using Lycia.Infrastructure.Eventing;
using Lycia.Infrastructure.Stores;
using Lycia.Messaging.Enums;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Sample.Shared.Messages.Commands;
using Sample.Shared.Messages.Events;
using Sample.Shared.Messages.Sagas;

namespace Lycia.Tests;

public class SagaDispatcherTests
{
    [Fact]
    public async Task DispatchAsync_Should_Invoke_CreateOrderSagaHandler()
    {
        // Arrange
        var services = new ServiceCollection();
        
        var fixedSagaId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        services.AddScoped<ISagaIdGenerator>(_ => new TestSagaIdGenerator(fixedSagaId));
        
        services.AddScoped<ISagaDispatcher, SagaDispatcher>();
        services.AddScoped<ISagaStore, InMemorySagaStore>();
        services.AddScoped<IEventBus>(sp =>
            new InMemoryEventBus(new Lazy<ISagaDispatcher>(sp.GetRequiredService<ISagaDispatcher>)));

        // Register handlers
        services.AddScoped<ISagaStartHandler<CreateOrderCommand>, CreateOrderSagaHandler>();
        services.AddScoped<ISagaStartHandler<OrderCreatedEvent>, ShipOrderSagaHandler>();
        services.AddScoped<ISagaStartHandler<OrderShippedEvent>, DeliverOrderSagaHandler>();

        var serviceProvider = services.BuildServiceProvider();
        var eventBus = serviceProvider.GetRequiredService<IEventBus>();
        var sagaStore = serviceProvider.GetRequiredService<ISagaStore>() as InMemorySagaStore;

        var command = new CreateOrderCommand
        {
            OrderId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            TotalPrice = 100
        };

        // Act
        // Instead of directly dispatching, publish the command via event bus and mark the step as completed
        await eventBus.Send(command);

        // Wait for saga steps to be processed (if needed)
        // For testing purposes, we can simulate step completion by checking the saga store

        // Assert
        var wasLogged = await sagaStore!.IsStepCompletedAsync(fixedSagaId, typeof(CreateOrderCommand));
        Assert.True(wasLogged);
        var wasShipped = await sagaStore.IsStepCompletedAsync(fixedSagaId, typeof(OrderCreatedEvent));
        var wasDelivered = await sagaStore.IsStepCompletedAsync(fixedSagaId, typeof(OrderShippedEvent));
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
        services.AddScoped<ISagaStore, InMemorySagaStore>();
        services.AddScoped<ISagaDispatcher, SagaDispatcher>();
        services.AddScoped<IEventBus>(sp =>
            new InMemoryEventBus(new Lazy<ISagaDispatcher>(sp.GetRequiredService<ISagaDispatcher>)));

        // Register all relevant SagaHandlers
        services.AddScoped<ISagaStartHandler<CreateOrderCommand>, CreateOrderSagaHandler>();
        services.AddScoped<ISagaStartHandler<OrderCreatedEvent>, ShipOrderForCompensationSagaHandler>();

        var provider = services.BuildServiceProvider();

        var dispatcher = provider.GetRequiredService<ISagaDispatcher>();
        var store = provider.GetRequiredService<ISagaStore>();

        var command = new CreateOrderCommand
        {
            OrderId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            TotalPrice = 99.0m,
            MessageId = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow
        };

        // Act
        await dispatcher.DispatchAsync(command);

        var steps = await store.GetSagaStepsAsync(fixedSagaId);

        // Assert - normal forward chain
        Assert.Contains(steps.Keys, x => x.Contains(nameof(CreateOrderCommand)));
        Assert.Contains(steps.Keys, x => x.Contains(nameof(OrderCreatedEvent)));

        Assert.All(steps.Values, meta =>
            Assert.True(meta.Status is StepStatus.Completed or StepStatus.Failed));
    }
    
    [Fact]
    public async Task DispatchAsync_NoHandlerRegistered_ShouldNotThrow()
    {
        var services = new ServiceCollection();
        services.AddScoped<ISagaIdGenerator, TestSagaIdGenerator>();
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
        services.AddScoped<ISagaStore, InMemorySagaStore>();
        services.AddScoped<ISagaDispatcher, SagaDispatcher>();
        services.AddScoped<IEventBus>(sp =>
            new InMemoryEventBus(new Lazy<ISagaDispatcher>(sp.GetRequiredService<ISagaDispatcher>)));

        // The ShipOrderForCompensationSagaHandler will intentionally fail at this step.
        services.AddScoped<ISagaStartHandler<CreateOrderCommand>, CreateOrderSagaHandler>();
        services.AddScoped<ISagaStartHandler<OrderCreatedEvent>, ShipOrderForCompensationSagaHandler>();

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

        // Assert - check the status of the steps in the chain
        var steps = await store.GetSagaStepsAsync(fixedSagaId);
        Assert.Contains(steps.Keys, x => x.Contains(nameof(CreateOrderCommand)));
        Assert.Contains(steps.Keys, x => x.Contains(nameof(OrderCreatedEvent)));
        // The ShipOrderForCompensationSagaHandler step should fail, and compensation should be triggered.
        Assert.Contains(steps.Values, meta => meta.Status == StepStatus.Failed);


        var handler = provider.GetRequiredService<CreateOrderSagaHandler>();
        // Was the compensation logic triggered?
        Assert.True(handler.CompensateCalled); 
        Assert.Contains(steps.Values, meta => meta.Status == StepStatus.Compensated);
    }
    
    [Fact]
    public async Task DispatchAsync_AllSteps_ShouldBeProcessedInOrder()
    {
        var services = new ServiceCollection();
        var fixedSagaId = Guid.NewGuid();
        services.AddScoped<ISagaIdGenerator>(_ => new TestSagaIdGenerator(fixedSagaId));
        services.AddScoped<ISagaStore, InMemorySagaStore>();
        services.AddScoped<ISagaDispatcher, SagaDispatcher>();
        services.AddScoped<IEventBus>(sp =>
            new InMemoryEventBus(new Lazy<ISagaDispatcher>(sp.GetRequiredService<ISagaDispatcher>)));

        services.AddScoped<ISagaStartHandler<CreateOrderCommand>, CreateOrderSagaHandler>();
        services.AddScoped<ISagaStartHandler<OrderCreatedEvent>, ShipOrderSagaHandler>();
        services.AddScoped<ISagaStartHandler<OrderShippedEvent>, DeliverOrderSagaHandler>();

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
        var steps = await store.GetSagaStepsAsync(fixedSagaId);

        var expectedStepTypes = new[] {
            nameof(CreateOrderCommand),
            nameof(OrderCreatedEvent),
            nameof(OrderShippedEvent)
        };

        foreach (var expected in expectedStepTypes)
            Assert.Contains(steps.Keys, x => x.Contains(expected));

        Assert.All(steps, kv => Assert.True(kv.Value.Status == StepStatus.Completed));
    }
    
    [Fact]
    public async Task DispatchAsync_DuplicateStep_ShouldNotProcessTwice()
    {
        var services = new ServiceCollection();
        var fixedSagaId = Guid.NewGuid();
        services.AddScoped<ISagaIdGenerator>(_ => new TestSagaIdGenerator(fixedSagaId));
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
        await dispatcher.DispatchAsync(command);

        // Assert: The same step should only be processed once.
        var steps = await store.GetSagaStepsAsync(fixedSagaId);
        Assert.Equal(1, steps.Count(x => x.Key.Contains(nameof(CreateOrderCommand))));
    }
    
    
}