using FluentAssertions;
using Lycia.Extensions.Configurations;
using StackExchange.Redis;
using Testcontainers.Redis;
using Lycia.Extensions.Stores;
using Lycia.Messaging;
using Lycia.Messaging.Enums;
using Lycia.Saga;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Configurations;
using Lycia.Saga.Exceptions;
using Lycia.Saga.Handlers;
using Lycia.Tests.Helpers;
using Microsoft.Extensions.Options;

namespace Lycia.IntegrationTests;

public class RedisSagaStoreIntegrationTests : IAsyncLifetime
{
    private readonly RedisContainer _redisContainer = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .WithCleanUp(true)
        .Build();

    private IDatabase _db = null!;

    private RedisSagaStore _store = null!;

    private readonly SagaStoreOptions _storeOptions = new()
    {
        ApplicationId = "TestApp",
        StepLogTtl = TimeSpan.FromMinutes(5)
    };

    public async Task InitializeAsync()
    {
        await _redisContainer.StartAsync();
        var connectionString = _redisContainer.GetConnectionString();
        //var connectionString = "127.0.0.1:6379";
        var redis = await ConnectionMultiplexer.ConnectAsync(connectionString);
        _db = redis.GetDatabase();

        _store = new RedisSagaStore(_db, null!, null!, null!, _storeOptions);
    }

    public async Task DisposeAsync()
    {
        await _redisContainer.DisposeAsync();
    }

    [Fact]
    public async Task HandleStart_Cancellation_Invokes_MarkAsCancelled_On_Context()
    {
        // Arrange
        var handler = new TestCancelHandler();
        var sagaOptions = Options.Create(new SagaOptions());

        var evt = new TestEvent { SagaId = Guid.NewGuid(), Message = "will cancel" };
        var stepType = typeof(TestEvent);
        var handlerType = typeof(TestCancelHandler);

        // 1) pre-log as Completed (simulate “already written” snapshot)
        await _store.LogStepAsync(
            sagaId: evt.SagaId!.Value,
            messageId: evt.MessageId,
            parentMessageId: evt.ParentMessageId,
            stepType: stepType,
            status: StepStatus.Completed,
            handlerType: handlerType,
            payload: new { msg = "pre-completed" },
            failureInfo: (SagaStepFailureInfo?)null
        );

        var ctx = new SagaContext<IMessage>(
            sagaId: evt.SagaId!.Value,
            currentStep: evt,
            handlerTypeOfCurrentStep: handlerType,
            eventBus: null!,
            sagaStore: _store,
            sagaIdGenerator: null!,
            compensationCoordinator: null!
        );

        handler.Initialize(ctx, sagaOptions);

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // trigger cancellation immediately

        // Act
        await handler.RunAsync(evt, cts.Token);
        
        // Assert
        var final = await _store.GetStepStatusAsync(evt.SagaId!.Value, evt.MessageId, stepType, handlerType);
        final.Should().Be(StepStatus.Cancelled);
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
            new { Value = 1 }, (SagaStepFailureInfo?)null);

        // CAS: Try to set again with the same status (idempotent, should succeed)
        var currentStatus = await store.GetStepStatusAsync(sagaId, messageId, stepType, handlerType);
        currentStatus.Should().Be(StepStatus.Started);

        // Move status to Completed (with correct previous value)
        await store.LogStepAsync(sagaId, messageId, parentMessageId, stepType, StepStatus.Completed, handlerType,
            new { Value = 2 }, (SagaStepFailureInfo?)null);
        var afterCompleted = await store.GetStepStatusAsync(sagaId, messageId, stepType, handlerType);
        afterCompleted.Should().Be(StepStatus.Completed);

        // Try to update with an invalid previous status (e.g., try to set back to Started)
        Func<Task> invalidTransition = () =>
            store.LogStepAsync(sagaId, messageId, parentMessageId, stepType, StepStatus.Started, handlerType,
                new { Value = 3 }, (SagaStepFailureInfo?)null);

        await invalidTransition.Should().ThrowAsync<SagaStepTransitionException>();

        // Update again with the correct status (idempotency test)
        await store.LogStepAsync(sagaId, messageId, parentMessageId, stepType, StepStatus.Completed, handlerType,
            new { Value = 2 }, (SagaStepFailureInfo?)null);
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
            new { Value = 123 }, (SagaStepFailureInfo?)null);

        (await store.GetStepStatusAsync(sagaId, messageId, stepType, handlerType))
            .Should().Be(StepStatus.Started);

        // Status transition test
        await store.LogStepAsync(sagaId, messageId, parentMessageId, stepType, StepStatus.Completed, handlerType,
            new { Value = 456 }, (SagaStepFailureInfo?)null);
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

        await store.LogStepAsync(sagaId, messageId, parentMessageId, stepType, StepStatus.Completed, handlerType, null,
            (SagaStepFailureInfo?)null);

        // Attempt to revert to Started (illegal)
        Func<Task> act = () =>
            store.LogStepAsync(sagaId, messageId, parentMessageId, stepType, StepStatus.Started, handlerType, null,
                (SagaStepFailureInfo?)null);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SaveSagaData_And_LoadSagaData_Works()
    {
        var sagaId = Guid.NewGuid();
        var data = new DummySagaData { };

        var sagaStoreOptions = new SagaStoreOptions
        {
            ApplicationId = "TestApp",
            StepLogTtl = TimeSpan.FromMinutes(5) // Set a TTL for the messages
        };

        var store = new RedisSagaStore(_db, null!, null!, null!, sagaStoreOptions);

        await store.SaveSagaDataAsync(sagaId, data);

        var loaded = await store.LoadSagaDataAsync<DummySagaData>(sagaId);

        loaded.Should().NotBeNull();
        loaded.Should().BeEquivalentTo(data);
    }

    private class TestEvent : EventBase
    {
        public string Message { get; set; } = string.Empty;
    }

    private class TestCancelHandler : StartReactiveSagaHandler<TestEvent>
    {
        public Task RunAsync(TestEvent message, CancellationToken ct) =>
            HandleAsyncInternal(message, ct);

        public override async Task HandleStartAsync(TestEvent message, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await Task.Delay(10, cancellationToken);
        }
    }
}