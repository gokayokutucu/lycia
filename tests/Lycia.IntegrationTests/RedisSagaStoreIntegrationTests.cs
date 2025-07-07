using FluentAssertions;
using StackExchange.Redis;
using Testcontainers.Redis;
using Lycia.Extensions.Stores;
using Lycia.Messaging;
using Lycia.Messaging.Attributes;
using Lycia.Messaging.Enums;
using Lycia.Saga;

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
        var redis = await ConnectionMultiplexer.ConnectAsync(_redisContainer.GetConnectionString());
        _db = redis.GetDatabase();
    }

    public async Task DisposeAsync()
    {
        await _redisContainer.DisposeAsync();
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

        var store = new RedisSagaStore(_db, null!, null!, null!);

        // Act & Assert
        await store.LogStepAsync(sagaId, messageId, parentMessageId, stepType, StepStatus.Started, handlerType, new { Value = 123 });

        (await store.GetStepStatusAsync(sagaId, messageId, stepType, handlerType))
            .Should().Be(StepStatus.Started);

        // Status transition test
        await store.LogStepAsync(sagaId, messageId, parentMessageId, stepType, StepStatus.Completed, handlerType, new { Value = 456 });
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

        var store = new RedisSagaStore(_db, null!, null!, null!);

        await store.LogStepAsync(sagaId, messageId, parentMessageId, stepType, StepStatus.Completed, handlerType);

        // Attempt to revert to Started (illegal)
        Func<Task> act = () => store.LogStepAsync(sagaId, messageId, parentMessageId, stepType, StepStatus.Started, handlerType);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SaveSagaData_And_LoadSagaData_Works()
    {
        var sagaId = Guid.NewGuid();
        var data = new SagaData { };

        var store = new RedisSagaStore(_db, null!, null!, null!);

        await store.SaveSagaDataAsync(sagaId, data);

        var loaded = await store.LoadSagaDataAsync(sagaId);

        loaded.Should().NotBeNull();
        loaded.Should().BeEquivalentTo(data);
    }

    [ApplicationId("TestApp")]
    private class DummyStep : EventBase { }
    private class DummyHandler { }
}