using Lycia.Infrastructure.Stores;
using Lycia.Messaging;
using Lycia.Messaging.Enums;
using Lycia.Saga.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Lycia.Infrastructure.Compensating;
using Lycia.Messaging.Utility;
using Lycia.Tests.Messages;

namespace Lycia.Tests;

public class SagaSagaStoreTests
{
    [Fact]
    public async Task LogStepAsync_Should_Not_Throw_For_Valid_Transitions()
    {
        var sagaId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var eventBus = new DummyEventBus();
        var sagaIdGenerator = new TestSagaIdGenerator();

        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var compensationCoordinator = new SagaCompensationCoordinator(serviceProvider);

        var store = new InMemorySagaStore(eventBus, sagaIdGenerator, compensationCoordinator);
        var stepType = typeof(DummyEvent);
        var handlerType = typeof(DummySagaHandler);

        await store.LogStepAsync(sagaId, messageId, messageId, stepType, StepStatus.Started, handlerType);
        await store.LogStepAsync(sagaId, messageId, messageId, stepType, StepStatus.Failed, handlerType);
        await store.LogStepAsync(sagaId, messageId, messageId, stepType, StepStatus.Compensated, handlerType);
    }

    [Fact]
    public async Task LogStepAsync_Should_Throw_When_CompensationFailed_To_Compensated_Transition()
    {
        // Arrange
        var sagaId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var eventBus = new DummyEventBus();
        var sagaIdGenerator = new TestSagaIdGenerator();

        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var compensationCoordinator = new SagaCompensationCoordinator(serviceProvider);

        var store = new InMemorySagaStore(eventBus, sagaIdGenerator, compensationCoordinator);
        var stepType = typeof(DummyEvent);
        var handlerType = typeof(DummySagaHandler);

        await store.LogStepAsync(sagaId, messageId, messageId, stepType, StepStatus.CompensationFailed, handlerType);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.LogStepAsync(sagaId, messageId, messageId, stepType, StepStatus.Compensated, handlerType));
    }

    [Fact]
    public async Task LogStepAsync_Should_Throw_When_Failed_To_Completed_Transition()
    {
        // Arrange
        var sagaId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var eventBus = new DummyEventBus();
        var sagaIdGenerator = new TestSagaIdGenerator();

        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var compensationCoordinator = new SagaCompensationCoordinator(serviceProvider);

        var store = new InMemorySagaStore(eventBus, sagaIdGenerator, compensationCoordinator);
        var stepType = typeof(DummyEvent);
        var handlerType = typeof(DummySagaHandler);

        await store.LogStepAsync(sagaId, messageId, messageId, stepType, StepStatus.Failed, handlerType);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.LogStepAsync(sagaId, messageId, messageId, stepType, StepStatus.Completed, handlerType));
    }

    [Fact]
    public async Task LogStepAsync_Should_Throw_When_Started_To_CompensationFailed_Transition()
    {
        // Arrange
        var sagaId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var eventBus = new DummyEventBus();
        var sagaIdGenerator = new TestSagaIdGenerator();

        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var compensationCoordinator = new SagaCompensationCoordinator(serviceProvider);

        var store = new InMemorySagaStore(eventBus, sagaIdGenerator, compensationCoordinator);
        var stepType = typeof(DummyEvent);
        var handlerType = typeof(DummySagaHandler);

        await store.LogStepAsync(sagaId, messageId, messageId, stepType, StepStatus.Started, handlerType);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.LogStepAsync(sagaId, messageId, messageId, stepType, StepStatus.CompensationFailed, handlerType));
    }

    [Fact]
    public async Task LogStepAsync_Should_Throw_When_Compensated_To_Completed_Transition()
    {
        // Arrange
        var sagaId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var eventBus = new DummyEventBus();
        var sagaIdGenerator = new TestSagaIdGenerator();

        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var compensationCoordinator = new SagaCompensationCoordinator(serviceProvider);

        var store = new InMemorySagaStore(eventBus, sagaIdGenerator, compensationCoordinator);
        var stepType = typeof(DummyEvent);
        var handlerType = typeof(DummySagaHandler);

        await store.LogStepAsync(sagaId, messageId, messageId, stepType, StepStatus.Compensated, handlerType);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.LogStepAsync(sagaId, messageId, messageId, stepType, StepStatus.Completed, handlerType));
    }

    [Fact]
    public async Task LogStepAsync_Should_Throw_When_Repeating_Completed_Transition()
    {
        // Arrange
        var sagaId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var eventBus = new DummyEventBus();
        var sagaIdGenerator = new TestSagaIdGenerator();

        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var compensationCoordinator = new SagaCompensationCoordinator(serviceProvider);

        var store = new InMemorySagaStore(eventBus, sagaIdGenerator, compensationCoordinator);
        var stepType = typeof(DummyEvent);
        var handlerType = typeof(DummySagaHandler);

        await store.LogStepAsync(sagaId, messageId, messageId, stepType, StepStatus.Completed, handlerType);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.LogStepAsync(sagaId, messageId, messageId, stepType, StepStatus.Completed, handlerType));
    }

    [Fact]
    public async Task LogStepAsync_Should_Prevent_Duplicate_Transitions_When_Concurrent()
    {
        // Arrange
        var sagaId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var eventBus = new DummyEventBus();
        var sagaIdGenerator = new TestSagaIdGenerator();

        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var compensationCoordinator = new SagaCompensationCoordinator(serviceProvider);

        var store = new InMemorySagaStore(eventBus, sagaIdGenerator, compensationCoordinator);
        var stepType = typeof(DummyEvent);
        var handlerType = typeof(DummySagaHandler);

        // Act
        await store.LogStepAsync(sagaId, messageId, messageId, stepType, StepStatus.Started, handlerType);

        InvalidOperationException? expected = null;
        Task? t1 = null, t2 = null;

        try
        {
            t1 = store.LogStepAsync(sagaId, messageId, messageId, stepType, StepStatus.Completed, handlerType);
        }
        catch (InvalidOperationException ex)
        {
            expected = ex;
        }

        try
        {
            t2 = store.LogStepAsync(sagaId, messageId, messageId, stepType, StepStatus.Completed, handlerType);
        }
        catch (InvalidOperationException ex)
        {
            expected = ex;
        }

        Assert.NotNull(expected);

        if (t1 != null) await t1;
        if (t2 != null) await t2;

        var steps = await store.GetSagaHandlerStepsAsync(sagaId);
        var completedCount = steps.Values.Count(meta => meta.Status == StepStatus.Completed);
        Assert.Equal(1, completedCount);
    }

    private class DummyEventBus : IEventBus
    {
        public Task Send<TCommand>(TCommand command, Guid? sagaId = null) where TCommand : ICommand =>
            Task.CompletedTask;

        public Task Publish<TEvent>(TEvent @event, Guid? sagaId = null) where TEvent : IEvent => Task.CompletedTask;

        public IAsyncEnumerable<(byte[] Body, Type MessageType)> ConsumeAsync(CancellationToken cancellationToken)
        {
            return default;
        }
    }

    // Dummy types for test isolation
    private class DummyEvent : IMessage
    {
        protected DummyEvent(Guid? parentMessageId = null, Guid? correlationId = null, string? applicationId = null)
        {
            MessageId = Guid.CreateVersion7();
            ParentMessageId = parentMessageId ?? Guid.Empty;
            CorrelationId = correlationId ?? MessageId;
            Timestamp = DateTime.UtcNow;
            ApplicationId = applicationId ?? EventMetadata.ApplicationId;
        }

        public Guid MessageId { get; }
        public Guid ParentMessageId { get; }
        public Guid CorrelationId
        {
            get;
#if NET6_0_OR_GREATER
            init;
#else
            set;
#endif
        }
        public DateTime Timestamp { get; }
        public string ApplicationId { get; }
        public Guid? SagaId { get; set; }
        public StepStatus? __TestStepStatus { get; set; }
        public Type? __TestStepType { get; set; }
    }

    private class DummySagaHandler
    {
    }
}