using System.Text.Json;
using FluentAssertions;
using StackExchange.Redis;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;
using Lycia.Messaging;
using Lycia.Extensions.Configurations;
using Lycia.Extensions.Eventing;
using Lycia.Extensions.Stores;
using Lycia.Messaging.Enums;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Handlers;
using Lycia.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lycia.IntegrationTests;

/// <summary>
/// Integration test for Saga compensation chain using RabbitMQ and Redis.
/// </summary>
public class RabbitMqSagaCompensationIntegrationTests : IAsyncLifetime
{
    private readonly RabbitMqContainer _rabbitMqContainer = new RabbitMqBuilder()
        .WithImage("rabbitmq:3.13-management-alpine")
        .WithCleanUp(true)
        .Build();

    private readonly RedisContainer _redisContainer = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .WithCleanUp(true)
        .Build();

    private string RabbitMqConnectionString => "amqp://guest:guest@127.0.0.1:5672/";// _rabbitMqContainer.GetConnectionString();
    private string RedisEndpoint => "127.0.0.1:6379";//$"{_redisContainer.Hostname}:{_redisContainer.GetMappedPublicPort(6379)}";

    public async Task InitializeAsync()
    {
        await _rabbitMqContainer.StartAsync();
        await _redisContainer.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _rabbitMqContainer.DisposeAsync();
        await _redisContainer.DisposeAsync();
    }

    [Fact]
    public async Task SagaChain_Should_Compensate_On_Failure()
    {
        // Arrange: Set up EventBus and Redis-backed SagaStore
        var applicationId = "TestApp";
        var handlerType = typeof(FailingSagaHandler);

        var queueName =
            Saga.Helpers.MessagingNamingHelper.GetRoutingKey(typeof(TestSagaCommand), handlerType, applicationId);
        var queueTypeMap = new Dictionary<string, Type> { { queueName, typeof(TestSagaCommand) } };
        var eventBusOptions = new EventBusOptions
            { ApplicationId = applicationId, MessageTTL = TimeSpan.FromSeconds(30) };

        var eventBus = await RabbitMqEventBus.CreateAsync(
            RabbitMqConnectionString,
            NullLogger<RabbitMqEventBus>.Instance,
            queueTypeMap,
            eventBusOptions);

        // Set up Redis connection and store
        var redis = await ConnectionMultiplexer.ConnectAsync(RedisEndpoint);
        var redisDb = redis.GetDatabase();

        var sagaStoreOptions = new SagaStoreOptions
        {
            ApplicationId = applicationId,
            StepLogTtl = TimeSpan.FromMinutes(5)
        };

        var dummySagaIdGenerator = new TestSagaIdGenerator(Guid.Parse("C6B819C0-98E6-4A3C-AD28-385F7ACF3E1D"));
        var dummyCompensationCoordinator = new DummySagaCompensationCoordinator();
        var sagaStore = new RedisSagaStore(redisDb, eventBus, dummySagaIdGenerator, dummyCompensationCoordinator,
            sagaStoreOptions);

        var testCommand = new TestSagaCommand
        {
            SagaId = Guid.NewGuid(),
            Message = "trigger-failure"
        };

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var receivedMessages = new List<TestSagaCommand>();

        var starterMessageId = Guid.NewGuid();
        // Simulate log "Started"
        await sagaStore.LogStepAsync(testCommand.SagaId.Value, starterMessageId, null, typeof(TestSagaCommand),
            StepStatus.Started, handlerType, testCommand);

        var finished = new TaskCompletionSource<bool>();

        var consumerTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var (body, type) in eventBus.ConsumeAsync(cancellationToken: cts.Token))
                {
                    if (JsonSerializer.Deserialize(body, type) is not TestSagaCommand msg) continue;
                    receivedMessages.Add(msg);

                    // Log step as Failed on exception
                    if (msg.Message == "trigger-failure")
                    {
                        await sagaStore.LogStepAsync(msg.SagaId.Value, starterMessageId, null, typeof(TestSagaCommand),
                            StepStatus.Failed, handlerType, msg);
                        finished.TrySetResult(true);
                        throw new InvalidOperationException("Intentional failure for compensation.");
                    }
                }
            }
            catch
            {
                finished.TrySetResult(true); // Any exception (inc. cancellation) also ends the test
            }
        });

        await eventBus.Send(testCommand, handlerType: handlerType);

        await Task.WhenAny(finished.Task, Task.Delay(5000, cts.Token));
        
        if (!finished.Task.IsCompleted)
        {
            throw new TimeoutException("TestSagaCommand was not processed in time!");
        }

        cts.Cancel();
        await consumerTask;
        
        
        await Task.Delay(2000);

        // Assert: Message should have been received
        receivedMessages.Should().ContainSingle(x => x.Message == "trigger-failure");

        // Assert: Redis log should contain Failed adım
        var sagaSteps = await sagaStore.GetSagaHandlerStepsAsync(testCommand.SagaId.Value);
        sagaSteps.Should().Contain(x => x.Value.Status == StepStatus.Failed);

        await eventBus.DisposeAsync();
    }

    // Dummy saga command
    private class TestSagaCommand : CommandBase
    {
        public string Message { get; set; } = string.Empty;
    }

    // Dummy handler that always throws (simulates saga failure and compensation path)
    private class FailingSagaHandler : ReactiveSagaHandler<TestSagaCommand>
    {
        public override Task HandleAsync(TestSagaCommand message)
        {
            throw new InvalidOperationException("Intentional failure for test.");
        }
    }
}

// Minimal dummy ISagaCompensationCoordinator implementation for testing.
internal class DummySagaCompensationCoordinator : ISagaCompensationCoordinator
{
    public Task CompensateAsync(Guid sagaId, Type failedStepType)
    {
        throw new NotImplementedException();
    }

    public Task CompensateParentAsync(Guid sagaId, Type stepType, IMessage message)
    {
        throw new NotImplementedException();
    }
}