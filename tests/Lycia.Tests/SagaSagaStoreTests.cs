// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Common.Enums;
using Lycia.Common.SagaSteps;
using Lycia.Extensions.Configurations;
using Lycia.Extensions.Stores;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Exceptions;
using Lycia.Stores;
using Lycia.Tests.Messages;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace Lycia.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RedisSagaStoreCollection : ICollectionFixture<RedisSagaStoreFixture>
{
    public const string Name = "Redis saga store";
}

public sealed class RedisSagaStoreFixture : IAsyncLifetime
{
    private readonly RedisContainer _container = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .WithCleanUp(true)
        .Build();

    private ConnectionMultiplexer? _connection;
    public IDatabase Database { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _connection = await ConnectionMultiplexer.ConnectAsync(_container.GetConnectionString());
        Database = _connection.GetDatabase();
    }

    public async Task DisposeAsync()
    {
        if (_connection is not null)
            await _connection.DisposeAsync();
        await _container.DisposeAsync();
    }
}

[Collection(RedisSagaStoreCollection.Name)]
public class SagaSagaStoreTests(RedisSagaStoreFixture redisFixture)
{
    private IDatabase RedisDatabase => redisFixture.Database;

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
            "Redis" => new RedisSagaStore(RedisDatabase, null!, null!, null!, sagaStoreOptions),
            "InMemory" => new InMemorySagaStore(null!, null!, null!),
            _ => throw new ArgumentOutOfRangeException()
        };
        var stepType = typeof(DummyEvent);
        var handlerType = typeof(DummySagaHandler);

        await store.LogStepAsync(sagaId, messageId, parentMessageId, stepType, StepStatus.Started, handlerType, null, (SagaStepFailureInfo?)null);
        await store.LogStepAsync(sagaId, messageId, parentMessageId, stepType, StepStatus.Failed, handlerType, null, (SagaStepFailureInfo?)null);
        await store.LogStepAsync(sagaId, messageId, parentMessageId, stepType, StepStatus.Compensated, handlerType, null, (SagaStepFailureInfo?)null);
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
            "Redis" => new RedisSagaStore(RedisDatabase, null!, null!, null!, sagaStoreOptions),
            "InMemory" => new InMemorySagaStore(null!, null!, null!),
            _ => throw new ArgumentOutOfRangeException()
        };

        var stepType = typeof(DummyEvent);
        var handlerType = typeof(DummySagaHandler);

        await store.LogStepAsync(sagaId, messageId, parentMessageId, stepType, StepStatus.CompensationFailed,
            handlerType, null, (SagaStepFailureInfo?)null);

        // Act & Assert
        await Assert.ThrowsAsync<SagaStepTransitionException>(() =>
            store.LogStepAsync(sagaId, messageId, parentMessageId, stepType, StepStatus.Compensated, handlerType, null, (SagaStepFailureInfo?)null));
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
            "Redis" => new RedisSagaStore(RedisDatabase, null!, null!, null!, sagaStoreOptions),
            "InMemory" => new InMemorySagaStore(null!, null!, null!),
            _ => throw new ArgumentOutOfRangeException()
        };

        var stepType = typeof(DummyEvent);
        var handlerType = typeof(DummySagaHandler);

        await store.LogStepAsync(sagaId, messageId, parentMessageId, stepType, StepStatus.Failed, handlerType, null, (SagaStepFailureInfo?)null);

        // Act & Assert
        await Assert.ThrowsAsync<SagaStepTransitionException>(() =>
            store.LogStepAsync(sagaId, messageId, parentMessageId, stepType, StepStatus.Completed, handlerType, null, (SagaStepFailureInfo?)null));
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
            "Redis" => new RedisSagaStore(RedisDatabase, null!, null!, null!, sagaStoreOptions),
            "InMemory" => new InMemorySagaStore(null!, null!, null!),
            _ => throw new ArgumentOutOfRangeException()
        };
        var stepType = typeof(DummyEvent);
        var handlerType = typeof(DummySagaHandler);

        await store.LogStepAsync(sagaId, messageId, parentMessageId, stepType, StepStatus.Started, handlerType, null, (SagaStepFailureInfo?)null);

        // Act & Assert
        await Assert.ThrowsAsync<SagaStepTransitionException>(() =>
            store.LogStepAsync(sagaId, messageId, parentMessageId, stepType, StepStatus.CompensationFailed,
                handlerType, null, (SagaStepFailureInfo?)null));
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
            "Redis" => new RedisSagaStore(RedisDatabase, null!, null!, null!, sagaStoreOptions),
            "InMemory" => new InMemorySagaStore(null!, null!, null!),
            _ => throw new ArgumentOutOfRangeException()
        };
        var stepType = typeof(DummyEvent);
        var handlerType = typeof(DummySagaHandler);

        await store.LogStepAsync(sagaId, messageId, parentMessageId, stepType, StepStatus.Compensated, handlerType, null, (SagaStepFailureInfo?)null);

        // Act & Assert
        await Assert.ThrowsAsync<SagaStepTransitionException>(() =>
            store.LogStepAsync(sagaId, messageId, parentMessageId, stepType, StepStatus.Completed, handlerType, null, (SagaStepFailureInfo?)null));
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
            "Redis" => new RedisSagaStore(RedisDatabase, null!, null!, null!, sagaStoreOptions),
            "InMemory" => new InMemorySagaStore(null!, null!, null!),
            _ => throw new ArgumentOutOfRangeException()
        };
        var stepType = typeof(DummyEvent);
        var handlerType = typeof(DummySagaHandler);

        await store.LogStepAsync(sagaId, messageId, parentMessageId, stepType, StepStatus.Completed, handlerType, null, (SagaStepFailureInfo?)null);

        // Act & Assert
        await store.LogStepAsync(sagaId, messageId, parentMessageId, stepType, StepStatus.Completed, handlerType, null, (SagaStepFailureInfo?)null);
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
            "Redis" => new RedisSagaStore(RedisDatabase, null!, null!, null!, sagaStoreOptions),
            "InMemory" => new InMemorySagaStore(null!, null!, null!),
            _ => throw new ArgumentOutOfRangeException()
        };
        var stepType = typeof(DummyEvent);
        var handlerType = typeof(DummySagaHandler);

        // Act
        await store.LogStepAsync(sagaId, messageId, parentMessageId, stepType, StepStatus.Started, handlerType, null, (SagaStepFailureInfo?)null);

        await store.LogStepAsync(sagaId, messageId, parentMessageId, stepType, StepStatus.Completed, handlerType, null, (SagaStepFailureInfo?)null);
        
        await store.LogStepAsync(sagaId, messageId, parentMessageId, stepType, StepStatus.Completed, handlerType, null, (SagaStepFailureInfo?)null);


        var steps = await store.GetSagaHandlerStepsAsync(sagaId);
        var completedCount = steps.Values.Count(meta => meta.Status == StepStatus.Completed);
        Assert.Equal(1, completedCount);
    }
}
