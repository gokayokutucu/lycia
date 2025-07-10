using System.Text;
using FluentAssertions;
using Lycia.Extensions.Configurations;
using Lycia.Extensions.Eventing;
using Lycia.Messaging;
using Lycia.Saga.Handlers;
using Lycia.Saga.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.RabbitMq;

namespace Lycia.IntegrationTests;

public class RabbitMqEventBusIntegrationTests : IAsyncLifetime
{
    // Use the official RabbitMqContainer builder
    private readonly RabbitMqContainer _rabbitMqContainer =
        new RabbitMqBuilder()
            .WithImage("rabbitmq:3.13-management-alpine")
            .WithCleanUp(true)
            .Build();

    public async Task InitializeAsync()
        => await _rabbitMqContainer.StartAsync().ConfigureAwait(false);

    public async Task DisposeAsync()
        => await _rabbitMqContainer.DisposeAsync().ConfigureAwait(false);
    
    [Fact]
    public async Task PublishThenConsume_Event_Succeeds()
    {
        string amqpUri = "amqp://guest:guest@localhost:5672"; 
        //var amqpUri = _rabbitMqContainer.GetConnectionString();
        var applicationId = "TestApp";
        var handlerType = typeof(TestEventHandler);

        var queueTypeMap = new Dictionary<string, Type>
        {
            { MessagingNamingHelper.GetRoutingKey(typeof(TestEvent), handlerType, applicationId), typeof(TestEvent) }
        };

        var eventBusOptions = new EventBusOptions
        {
            ApplicationId = applicationId,
            MessageTTL = TimeSpan.FromMinutes(5)
        };

        var eventBus = await RabbitMqEventBus.CreateAsync(
            amqpUri,
            NullLogger<RabbitMqEventBus>.Instance,
            queueTypeMap,
            eventBusOptions);

        var testEvent = new TestEvent
        {
            SagaId = Guid.NewGuid(),
            Message = "Integration test message"
        };

        await eventBus.Publish(testEvent);
        await Task.Delay(500); // allow async delivery

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(300));
        bool received = false;

        await foreach (var (body, type) in eventBus.ConsumeAsync(cts.Token))
        {
            var json = Encoding.UTF8.GetString(body);
            var evt = System.Text.Json.JsonSerializer.Deserialize(json, type);

            evt.Should().BeOfType<TestEvent>();
            ((TestEvent)evt).Message.Should().Be("Integration test message");
            received = true;
            break;
        }

        received.Should().BeTrue();

        await eventBus.DisposeAsync();
    }
    
    [Fact]
    public async Task PublishThenConsume_Event_Succeeds2()
    {
        string amqpUri = "amqp://guest:guest@localhost:5672"; 
        var applicationId = "TestApp";
        var handlerType = typeof(TestEventHandler);

        var queueTypeMap = new Dictionary<string, Type>
        {
            { MessagingNamingHelper.GetRoutingKey(typeof(TestEvent), handlerType, applicationId), typeof(TestEvent) }
        };

        var eventBusOptions = new EventBusOptions
        {
            ApplicationId = applicationId,
            MessageTTL = TimeSpan.FromMinutes(5)
        };

        var eventBus = await RabbitMqEventBus.CreateAsync(
            amqpUri,
            NullLogger<RabbitMqEventBus>.Instance,
            queueTypeMap,
            eventBusOptions);

        // 1. Önce ConsumeAsync'i başlat (Background task olarak başlatmak daha iyi)
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var received = false;
        var consumeTask = Task.Run(async () =>
        {
            await foreach (var (body, type) in eventBus.ConsumeAsync(cts.Token))
            {
                var json = Encoding.UTF8.GetString(body);
                var evt = System.Text.Json.JsonSerializer.Deserialize(json, type);

                evt.Should().BeOfType<TestEvent>();
                ((TestEvent)evt).Message.Should().Be("Integration test message");
                received = true;
                break;
            }
        });

        // 2. Şimdi publish et
        var testEvent = new TestEvent
        {
            SagaId = Guid.NewGuid(),
            Message = "Integration test message"
        };
        await eventBus.Publish(testEvent);

        // 3. Consume işlemi tamamlanana kadar bekle
        await consumeTask;

        received.Should().BeTrue();

        await eventBus.DisposeAsync();
    }
    
    private class TestEventHandler : StartReactiveSagaHandler<TestEvent>
    {
        public override Task HandleStartAsync(TestEvent message)
        {
            return Task.CompletedTask;
        }
    }
    
    private class TestEvent : EventBase
    {
        public string Message { get; init; } = string.Empty;
    }
}