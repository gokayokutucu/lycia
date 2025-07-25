using Lycia.Extensions.Configurations;
using Lycia.Extensions.Stores;
using Lycia.Infrastructure.Stores;
using Lycia.Messaging.Enums;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Exceptions;
using Lycia.Tests.Messages;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace Lycia.Tests;

public class SagaSagaStoreTests : IAsyncLifetime
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

    [Theory]
    [InlineData("InMemory")]
    [InlineData("Redis")]
    public async Task LogStepAsync_Should_Not_Throw_For_Valid_Transitions(string storeType)
    {
        var sagaId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var parentMessageId = Guid.Empty;

        var sagaStoreOptions = new SagaStoreOptions
        {
            ApplicationId = "TestApp",
            StepLogTtl = TimeSpan.FromMinutes(5)
        };

        ISagaStore store = storeType switch
        {
            "Redis" => new RedisSagaStore(_db, null!, null!, null!, sagaStoreOptions),
            "InMemory" => new InMemorySagaStore(null!, null!, null!),
            _ => throw new ArgumentOutOfRangeException()
        };
        var stepType = typeof(DummyEvent);
        var handlerType = typeof(DummySagaHandler);

        await store.LogStepAsync(sagaId, messageId, parentMessageId, stepType, StepStatus.Started, handlerType);
        await store.LogStepAsync(sagaId, messageId, parentMessageId, stepType, StepStatus.Failed, handlerType);
        await store.LogStepAsync(sagaId, messageId, parentMessageId, stepType, StepStatus.Compensated, handlerType);
    }

    [Theory]
    [InlineData("InMemory")]
    [InlineData("Redis")]
    public async Task LogStepAsync_Should_Throw_When_CompensationFailed_To_Compensated_Transition(string storeType)
    {
        // Arrange
        var sagaId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var parentMessageId = Guid.Empty;

        var sagaStoreOptions = new SagaStoreOptions
        {
            ApplicationId = "TestApp",
            StepLogTtl = TimeSpan.FromMinutes(5)
        };


        ISagaStore store = storeType switch
        {
            "Redis" => new RedisSagaStore(_db, null!, null!, null!, sagaStoreOptions),
            "InMemory" => new InMemorySagaStore(null!, null!, null!),
            _ => throw new ArgumentOutOfRangeException()
        };

        var stepType = typeof(DummyEvent);
        var handlerType = typeof(DummySagaHandler);

        await store.LogStepAsync(sagaId, messageId, parentMessageId, stepType, StepStatus.CompensationFailed,
            handlerType);

        // Act & Assert
        await Assert.ThrowsAsync<SagaStepTransitionException>(() =>
            store.LogStepAsync(sagaId, messageId, parentMessageId, stepType, StepStatus.Compensated, handlerType));
    }

    [Theory]
    [InlineData("InMemory")]
    [InlineData("Redis")]
    public async Task LogStepAsync_Should_Throw_When_Failed_To_Completed_Transition(string storeType)
    {
        // Arrange
        var sagaId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var parentMessageId = Guid.Empty;

        var sagaStoreOptions = new SagaStoreOptions
        {
            ApplicationId = "TestApp",
            StepLogTtl = TimeSpan.FromMinutes(5)
        };

        ISagaStore store = storeType switch
        {
            "Redis" => new RedisSagaStore(_db, null!, null!, null!, sagaStoreOptions),
            "InMemory" => new InMemorySagaStore(null!, null!, null!),
            _ => throw new ArgumentOutOfRangeException()
        };

        var stepType = typeof(DummyEvent);
        var handlerType = typeof(DummySagaHandler);

        await store.LogStepAsync(sagaId, messageId, parentMessageId, stepType, StepStatus.Failed, handlerType);

        // Act & Assert
        await Assert.ThrowsAsync<SagaStepTransitionException>(() =>
            store.LogStepAsync(sagaId, messageId, parentMessageId, stepType, StepStatus.Completed, handlerType));
    }

    [Theory]
    [InlineData("InMemory")]
    [InlineData("Redis")]
    public async Task LogStepAsync_Should_Throw_When_Started_To_CompensationFailed_Transition(string storeType)
    {
        // Arrange
        var sagaId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var parentMessageId = Guid.Empty;

        var sagaStoreOptions = new SagaStoreOptions
        {
            ApplicationId = "TestApp",
            StepLogTtl = TimeSpan.FromMinutes(5)
        };

        ISagaStore store = storeType switch
        {
            "Redis" => new RedisSagaStore(_db, null!, null!, null!, sagaStoreOptions),
            "InMemory" => new InMemorySagaStore(null!, null!, null!),
            _ => throw new ArgumentOutOfRangeException()
        };
        var stepType = typeof(DummyEvent);
        var handlerType = typeof(DummySagaHandler);

        await store.LogStepAsync(sagaId, messageId, parentMessageId, stepType, StepStatus.Started, handlerType);

        // Act & Assert
        await Assert.ThrowsAsync<SagaStepTransitionException>(() =>
            store.LogStepAsync(sagaId, messageId, parentMessageId, stepType, StepStatus.CompensationFailed,
                handlerType));
    }

    [Theory]
    [InlineData("InMemory")]
    [InlineData("Redis")]
    public async Task LogStepAsync_Should_Throw_When_Compensated_To_Completed_Transition(string storeType)
    {
        // Arrange
        var sagaId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var parentMessageId = Guid.Empty;

        var sagaStoreOptions = new SagaStoreOptions
        {
            ApplicationId = "TestApp",
            StepLogTtl = TimeSpan.FromMinutes(5)
        };

        ISagaStore store = storeType switch
        {
            "Redis" => new RedisSagaStore(_db, null!, null!, null!, sagaStoreOptions),
            "InMemory" => new InMemorySagaStore(null!, null!, null!),
            _ => throw new ArgumentOutOfRangeException()
        };
        var stepType = typeof(DummyEvent);
        var handlerType = typeof(DummySagaHandler);

        await store.LogStepAsync(sagaId, messageId, parentMessageId, stepType, StepStatus.Compensated, handlerType);

        // Act & Assert
        await Assert.ThrowsAsync<SagaStepTransitionException>(() =>
            store.LogStepAsync(sagaId, messageId, parentMessageId, stepType, StepStatus.Completed, handlerType));
    }

    [Theory]
    [InlineData("InMemory")]
    [InlineData("Redis")]
    public async Task LogStepAsync_Should_Allow_Idempotent_Repeating_Completed_Transition(string storeType)
    {
        // Arrange
        var sagaId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var parentMessageId = Guid.Empty;

        var sagaStoreOptions = new SagaStoreOptions
        {
            ApplicationId = "TestApp",
            StepLogTtl = TimeSpan.FromMinutes(5)
        };

        ISagaStore store = storeType switch
        {
            "Redis" => new RedisSagaStore(_db, null!, null!, null!, sagaStoreOptions),
            "InMemory" => new InMemorySagaStore(null!, null!, null!),
            _ => throw new ArgumentOutOfRangeException()
        };
        var stepType = typeof(DummyEvent);
        var handlerType = typeof(DummySagaHandler);

        await store.LogStepAsync(sagaId, messageId, parentMessageId, stepType, StepStatus.Completed, handlerType);

        // Act & Assert
        await store.LogStepAsync(sagaId, messageId, parentMessageId, stepType, StepStatus.Completed, handlerType);
    }

    [Theory]
    [InlineData("InMemory")]
    [InlineData("Redis")]
    public async Task LogStepAsync_Should_Prevent_Duplicate_Transitions_When_Concurrent(string storeType)
    {
        // Arrange
        var sagaId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var parentMessageId = Guid.Empty;

        var sagaStoreOptions = new SagaStoreOptions
        {
            ApplicationId = "TestApp",
            StepLogTtl = TimeSpan.FromMinutes(5)
        };

        ISagaStore store = storeType switch
        {
            "Redis" => new RedisSagaStore(_db, null!, null!, null!, sagaStoreOptions),
            "InMemory" => new InMemorySagaStore(null!, null!, null!),
            _ => throw new ArgumentOutOfRangeException()
        };
        var stepType = typeof(DummyEvent);
        var handlerType = typeof(DummySagaHandler);

        // Act
        await store.LogStepAsync(sagaId, messageId, parentMessageId, stepType, StepStatus.Started, handlerType);

        await store.LogStepAsync(sagaId, messageId, parentMessageId, stepType, StepStatus.Completed, handlerType);
        
        await store.LogStepAsync(sagaId, messageId, parentMessageId, stepType, StepStatus.Completed, handlerType);


        var steps = await store.GetSagaHandlerStepsAsync(sagaId);
        var completedCount = steps.Values.Count(meta => meta.Status == StepStatus.Completed);
        Assert.Equal(1, completedCount);
    }
}