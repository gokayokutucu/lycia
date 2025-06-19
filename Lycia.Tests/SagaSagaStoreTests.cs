using Lycia.Infrastructure.Stores;
using Lycia.Messaging;
using Lycia.Messaging.Enums;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Extensions;

namespace Lycia.Tests;



public class SagaSagaStoreTests
{
    [Fact]
    public async Task LogStepAsync_Should_Not_Throw_For_Valid_Transitions()
    {
        var sagaId = Guid.NewGuid();
        var eventBus = new DummyEventBus();
        var sagaIdGenerator = new TestSagaIdGenerator();
        var store = new InMemorySagaStore(eventBus, sagaIdGenerator);
        var stepType = typeof(DummyEvent);
        var handlerType = typeof(DummySagaHandler);

        await store.LogStepAsync(sagaId, stepType, StepStatus.Started, handlerType);
        await store.LogStepAsync(sagaId, stepType, StepStatus.Failed, handlerType);
        await store.LogStepAsync(sagaId, stepType, StepStatus.Compensated, handlerType);
    }
    
    [Fact]
    public async Task LogStepAsync_Should_Throw_When_CompensationFailed_To_Compensated_Transition()
    {
        // Arrange
        var sagaId = Guid.NewGuid();
        var eventBus = new DummyEventBus();
        var sagaIdGenerator = new TestSagaIdGenerator();
        var store = new InMemorySagaStore(eventBus, sagaIdGenerator);
        var stepType = typeof(DummyEvent);
        var handlerType = typeof(DummySagaHandler);

        await store.LogStepAsync(sagaId, stepType, StepStatus.CompensationFailed, handlerType);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.LogStepAsync(sagaId, stepType, StepStatus.Compensated, handlerType));
    }
    
    [Fact]
    public async Task LogStepAsync_Should_Throw_When_Failed_To_Completed_Transition()
    {
        // Arrange
        var sagaId = Guid.NewGuid();
        var eventBus = new DummyEventBus();
        var sagaIdGenerator = new TestSagaIdGenerator();
        var store = new InMemorySagaStore(eventBus, sagaIdGenerator);
        var stepType = typeof(DummyEvent);
        var handlerType = typeof(DummySagaHandler);

        await store.LogStepAsync(sagaId, stepType, StepStatus.Failed, handlerType);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.LogStepAsync(sagaId, stepType, StepStatus.Completed, handlerType));
    }
    
    [Fact]
    public async Task LogStepAsync_Should_Throw_When_Started_To_CompensationFailed_Transition()
    {
        // Arrange
        var sagaId = Guid.NewGuid();
        var eventBus = new DummyEventBus();
        var sagaIdGenerator = new TestSagaIdGenerator();
        var store = new InMemorySagaStore(eventBus, sagaIdGenerator);
        var stepType = typeof(DummyEvent);
        var handlerType = typeof(DummySagaHandler);

        await store.LogStepAsync(sagaId, stepType, StepStatus.Started, handlerType);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.LogStepAsync(sagaId, stepType, StepStatus.CompensationFailed, handlerType));
    }
    
    [Fact]
    public async Task LogStepAsync_Should_Throw_When_Compensated_To_Completed_Transition()
    {
        // Arrange
        var sagaId = Guid.NewGuid();
        var eventBus = new DummyEventBus();
        var sagaIdGenerator = new TestSagaIdGenerator();
        var store = new InMemorySagaStore(eventBus, sagaIdGenerator);
        var stepType = typeof(DummyEvent);
        var handlerType = typeof(DummySagaHandler);

        await store.LogStepAsync(sagaId, stepType, StepStatus.Compensated, handlerType);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.LogStepAsync(sagaId, stepType, StepStatus.Completed, handlerType));
    }
    
    [Fact]
    public async Task LogStepAsync_Should_Throw_When_Repeating_Completed_Transition()
    {
        // Arrange
        var sagaId = Guid.NewGuid();
        var eventBus = new DummyEventBus();
        var sagaIdGenerator = new TestSagaIdGenerator();
        var store = new InMemorySagaStore(eventBus, sagaIdGenerator);
        var stepType = typeof(DummyEvent);
        var handlerType = typeof(DummySagaHandler);

        await store.LogStepAsync(sagaId, stepType, StepStatus.Completed, handlerType);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.LogStepAsync(sagaId, stepType, StepStatus.Completed, handlerType));
    }
    
    [Fact]
    public async Task LogStepAsync_Should_Prevent_Duplicate_Transitions_When_Concurrent()
    {
        // Arrange
        var sagaId = Guid.NewGuid();
        var eventBus = new DummyEventBus(); 
        var sagaIdGenerator = new TestSagaIdGenerator();
        var store = new InMemorySagaStore(eventBus, sagaIdGenerator);
        var stepType = typeof(DummyEvent);
        var handlerType = typeof(DummySagaHandler);

        // Act
        await store.LogStepAsync(sagaId, stepType, StepStatus.Started, handlerType);

        var t1 = store.LogStepAsync(sagaId, stepType, StepStatus.Completed, handlerType);
        var t2 = store.LogStepAsync(sagaId, stepType, StepStatus.Completed, handlerType);

        await Task.WhenAll(t1, t2);

        // Assert
        var steps = await store.GetSagaHandlerStepsAsync(sagaId);
        var completedCount = steps.Values.Count(meta => meta.Status == StepStatus.Completed);
        Assert.Equal(1, completedCount);
    }
    
    private class DummyEventBus : IEventBus
    {
        public Task Send<TCommand>(TCommand command, Guid? sagaId = null) where TCommand : ICommand => Task.CompletedTask;
        public Task Publish<TEvent>(TEvent @event, Guid? sagaId = null) where TEvent : IEvent => Task.CompletedTask;
        public IAsyncEnumerable<(byte[] Body, Type MessageType)> ConsumeAsync(CancellationToken cancellationToken)
        {
            return default;
        }
    }

    // Dummy types for test isolation
    private class DummyEvent : IMessage {
        public Guid MessageId { get; }
        public DateTime Timestamp { get; }
        public string ApplicationId { get; }
        public Guid? SagaId { get; set; }
        public StepStatus? __TestStepStatus { get; set; }
        public Type? __TestStepType { get; set; }
    }
    private class DummySagaHandler { }
}