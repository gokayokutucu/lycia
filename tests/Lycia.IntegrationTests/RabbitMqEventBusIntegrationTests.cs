using System.Text;
using FluentAssertions;
using Lycia.Extensions.Configurations;
using Lycia.Extensions.Eventing;
using Lycia.Messaging;
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
        // Arrange
        //string amqpUri = "amqp://guest:guest@localhost:5672";
        var amqpUri = _rabbitMqContainer.GetConnectionString();
        var queueTypeMap = new Dictionary<string, Type>
        {
            { RoutingKeyHelper.GetRoutingKey(typeof(TestEvent)), typeof(TestEvent) }
        };

        var eventBusOptions = new EventBusOptions
        {
            ApplicationId = "TestApp",
            MessageTTL = TimeSpan.FromMinutes(1) // Set a TTL for the messages
        };

        var eventBus = await RabbitMqEventBus.CreateAsync(
            amqpUri,
            NullLogger<RabbitMqEventBus>.Instance,
            queueTypeMap,
            eventBusOptions);

        var testEvent = new TestEvent { Message = "Integration test message" };

        // Act
        await eventBus.Publish(testEvent);
        await Task.Delay(500); // allow async delivery

        // Assert: consume with timeout
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        bool received = false;

        await foreach (var (body, type) in eventBus.ConsumeAsync(cts.Token))
        {
            var json = Encoding.UTF8.GetString(body);
            var evt = System.Text.Json.JsonSerializer.Deserialize(json, type);

            evt.Should().BeOfType<TestEvent>();
            ((TestEvent)evt!).Message.Should().Be("Integration test message");
            received = true;
            break;
        }

        received.Should().BeTrue();

        await eventBus.DisposeAsync();
    }
    
    private class TestEvent : EventBase
    {
        public string Message { get; init; } = string.Empty;
    }
}