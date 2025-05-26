using Lycia.Infrastructure.Abstractions;
using Lycia.Infrastructure.Dispatching;
using Lycia.Infrastructure.Eventing;
using Lycia.Infrastructure.Stores;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Enums;
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
        var dispatcher = serviceProvider.GetRequiredService<ISagaDispatcher>();

        var command = new CreateOrderCommand
        {
            OrderId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            TotalPrice = 100
        };

        // Act
        await dispatcher.DispatchAsync(command);

        // Assert
        var sagaStore = serviceProvider.GetRequiredService<ISagaStore>() as InMemorySagaStore;
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
}