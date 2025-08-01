using FluentAssertions;
using Lycia.Extensions.Configurations;
using StackExchange.Redis;
using Testcontainers.Redis;
using Lycia.Extensions.Stores;
using Lycia.Messaging;
using Lycia.Messaging.Enums;
using Lycia.Saga;
using Lycia.Saga.Exceptions;

namespace Lycia.IntegrationTests;

public class RedisSagaStoreIntegrationTests : IAsyncLifetime
{
    private readonly RedisContainer _redisContainer = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .WithCleanUp(true)
        .Build();

    private IDatabase _db = null!;

    public async Task InitializeAsync()
    {
        await _redisContainer.StartAsync();
        var connectionString = _redisContainer.GetConnectionString();
        //var connectionString = "127.0.0.1:6379";
        var redis = await ConnectionMultiplexer.ConnectAsync(connectionString);
        _db = redis.GetDatabase();
    }

    public async Task DisposeAsync()
    {
        await _redisContainer.DisposeAsync();
    }

    [Fact]
    public async Task LogStepAsync_Should_Be_Atomic_And_Idempotent()
    {
        // Arrange
        var sagaId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var parentMessageId = Guid.NewGuid();
        var stepType = typeof(DummyStep);
        var handlerType = typeof(DummyHandler);

        var sagaStoreOptions = new SagaStoreOptions
        {
            ApplicationId = "TestApp",
            StepLogTtl = TimeSpan.FromMinutes(5)
        };
        var store = new RedisSagaStore(_db, null!, null!, null!, sagaStoreOptions);

        // First set: Started
        await store.LogStepAsync(sagaId, messageId, parentMessageId, stepType, StepStatus.Started, handlerType,
            new { Value = 1 });

        // CAS: Try to set again with the same status (idempotent, should succeed)
        var currentStatus = await store.GetStepStatusAsync(sagaId, messageId, stepType, handlerType);
        currentStatus.Should().Be(StepStatus.Started);

        // Move status to Completed (with correct previous value)
        await store.LogStepAsync(sagaId, messageId, parentMessageId, stepType, StepStatus.Completed, handlerType,
            new { Value = 2 });
        var afterCompleted = await store.GetStepStatusAsync(sagaId, messageId, stepType, handlerType);
        afterCompleted.Should().Be(StepStatus.Completed);

        // Try to update with an invalid previous status (e.g., try to set back to Started)
        Func<Task> invalidTransition = () =>
            store.LogStepAsync(sagaId, messageId, parentMessageId, stepType, StepStatus.Started, handlerType,
                new { Value = 3 });

        await invalidTransition.Should().ThrowAsync<SagaStepTransitionException>();

        // Update again with the correct status (idempotency test)
        await store.LogStepAsync(sagaId, messageId, parentMessageId, stepType, StepStatus.Completed, handlerType,
            new { Value = 2 });
        var finalStatus = await store.GetStepStatusAsync(sagaId, messageId, stepType, handlerType);
        finalStatus.Should().Be(StepStatus.Completed);
    }

    [Fact]
    public async Task LogStep_And_GetStepStatus_Should_Work()
    {
        // Arrange
        var sagaId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var parentMessageId = Guid.NewGuid();
        var stepType = typeof(DummyStep);
        var handlerType = typeof(DummyHandler);

        var sagaStoreOptions = new SagaStoreOptions
        {
            ApplicationId = "TestApp",
            StepLogTtl = TimeSpan.FromMinutes(5) // Set a TTL for the messages
        };

        var store = new RedisSagaStore(_db, null!, null!, null!, sagaStoreOptions);

        // Act & Assert
        await store.LogStepAsync(sagaId, messageId, parentMessageId, stepType, StepStatus.Started, handlerType,
            new { Value = 123 });

        (await store.GetStepStatusAsync(sagaId, messageId, stepType, handlerType))
            .Should().Be(StepStatus.Started);

        // Status transition test
        await store.LogStepAsync(sagaId, messageId, parentMessageId, stepType, StepStatus.Completed, handlerType,
            new { Value = 456 });
        (await store.GetStepStatusAsync(sagaId, messageId, stepType, handlerType))
            .Should().Be(StepStatus.Completed);

        // IsStepCompleted test
        (await store.IsStepCompletedAsync(sagaId, messageId, stepType, handlerType))
            .Should().BeTrue();
    }

    [Fact]
    public async Task Should_Throw_On_Illegal_StepStatus_Transition()
    {
        var sagaId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var parentMessageId = Guid.NewGuid();
        var stepType = typeof(DummyStep);
        var handlerType = typeof(DummyHandler);

        var sagaStoreOptions = new SagaStoreOptions
        {
            ApplicationId = "TestApp",
            StepLogTtl = TimeSpan.FromMinutes(5) // Set a TTL for the messages
        };

        var store = new RedisSagaStore(_db, null!, null!, null!, sagaStoreOptions);

        await store.LogStepAsync(sagaId, messageId, parentMessageId, stepType, StepStatus.Completed, handlerType, null);

        // Attempt to revert to Started (illegal)
        Func<Task> act = () =>
            store.LogStepAsync(sagaId, messageId, parentMessageId, stepType, StepStatus.Started, handlerType, null);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SaveSagaData_And_LoadSagaData_Works()
    {
        var sagaId = Guid.NewGuid();
        var data = new DummyStore{ };

        var sagaStoreOptions = new SagaStoreOptions
        {
            ApplicationId = "TestApp",
            StepLogTtl = TimeSpan.FromMinutes(5) // Set a TTL for the messages
        };

        var store = new RedisSagaStore(_db, null!, null!, null!, sagaStoreOptions);

        await store.SaveSagaDataAsync(sagaId, data);

        var loaded = await store.LoadSagaDataAsync<DummyStore>(sagaId);

        loaded.Should().NotBeNull();
        loaded.Should().BeEquivalentTo(data);
    }

    private class DummyStore{}
    
    private class DummyStep : EventBase
    {
    }

    private class DummyHandler
    {
    }
}