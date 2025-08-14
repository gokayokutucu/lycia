using System.Text;
using FluentAssertions;
using Lycia.Extensions.Configurations;
using Lycia.Extensions.Eventing;
using Lycia.Messaging;
using Lycia.Saga.Handlers;
using Lycia.Saga.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
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
    public async Task Publish_Event_Expires_To_DLQ_Succeeds()
    {
        //string amqpUri = "amqp://guest:guest@localhost:5672";
        var amqpUri = _rabbitMqContainer.GetConnectionString();
        var applicationId = "TestApp";
        var handlerType = typeof(TestEventHandlerA);
        var queueName = MessagingNamingHelper.GetRoutingKey(typeof(TestEvent), handlerType, applicationId);

        // Clean up before test (best practice for integration tests)
        var factory = new ConnectionFactory { Uri = new Uri(amqpUri) };
        await using (var conn = await factory.CreateConnectionAsync(CancellationToken.None))
        await using (var channelDelete = await conn.CreateChannelAsync(cancellationToken: CancellationToken.None))
        {
            try
            {
                await channelDelete.QueueDeleteAsync(queueName);
            }
            catch
            {
                // ignored
            }

            try
            {
                await channelDelete.QueueDeleteAsync(queueName + ".dlq");
            }
            catch
            {
                // ignored
            }
        }

        var queueTypeMap = new Dictionary<string, (Type, Type)>
        {
            { queueName, (typeof(TestEvent), typeof(TestEventHandlerA)) }
        };

        var ttl = TimeSpan.FromSeconds(5);
        var eventBusOptions = new EventBusOptions
        {
            ApplicationId = applicationId,
            MessageTTL = ttl
        };

        await using (var consumerBus = await RabbitMqEventBus.CreateAsync(
                   amqpUri,
                   NullLogger<RabbitMqEventBus>.Instance,
                   queueTypeMap,
                   eventBusOptions))
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)); // Extra time for test

            try
            {
                // Trigger consumer (do not process any messages)
                // Start the consumer just to trigger queue creation, don't wait for any message
                await using var enumerator = consumerBus.ConsumeAsync(autoAck: false, cancellationToken: cts.Token).GetAsyncEnumerator(cts.Token);
                // Just trigger queue/DLQ creation (no actual message expected)
                await enumerator.MoveNextAsync();
            }
            catch (TaskCanceledException) { /* Ignore cancellation*/  }
            catch (OperationCanceledException) { /* Ignore cancellation */  }
        }
        
        await Task.Delay(500);

       await using (var publisherBus = await RabbitMqEventBus.CreateAsync(
                   amqpUri,
                   NullLogger<RabbitMqEventBus>.Instance,
                   queueTypeMap,
                   eventBusOptions))
        {
            // Publish event
            var testEvent = new TestEvent
            {
                SagaId = Guid.NewGuid(),
                Message = "DLQ Test Message"
            };
            await publisherBus.Publish(testEvent);
        }
       
        // Wait for TTL + DLQ transfer
        await Task.Delay(ttl + TimeSpan.FromSeconds(23));

        await using var conn2 = await factory.CreateConnectionAsync(CancellationToken.None);
        await using var channel = await conn2.CreateChannelAsync(cancellationToken: CancellationToken.None);

        var dlqName = queueName + ".dlq";

        var result = await channel.QueueDeclarePassiveAsync(dlqName, CancellationToken.None);
        result.MessageCount.Should().Be(1, "The message should be dead-lettered after TTL expires.");

        var dlqResult = await channel.BasicGetAsync(dlqName, autoAck: true, cancellationToken: CancellationToken.None);
        dlqResult.Should().NotBeNull();

        var body = Encoding.UTF8.GetString(dlqResult.Body.ToArray());
        body.Should().Contain("DLQ Test Message");
    }

    [Fact]
    public async Task PublishThenConsume_Event_Succeeds()
    {
        //string amqpUri = "amqp://guest:guest@localhost:5672";
        var amqpUri = _rabbitMqContainer.GetConnectionString();
        var applicationId = "TestApp";
        var handlerType = typeof(TestEventHandlerA);

        var queueTypeMap = new Dictionary<string, (Type, Type)>
        {
            { MessagingNamingHelper.GetRoutingKey(typeof(TestEvent), handlerType, applicationId), (typeof(TestEvent), typeof(TestEventHandlerA) )}
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

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        bool received = false;

        var consumeTask = Task.Run(async () =>
        {
            await foreach (var (body, messageType, _) in eventBus.ConsumeAsync(cancellationToken: cts.Token))
            {
                var json = Encoding.UTF8.GetString(body);
                var evt = System.Text.Json.JsonSerializer.Deserialize(json, messageType);

                evt.Should().BeOfType<TestEvent>();
                ((TestEvent)evt).Message.Should().Be("Integration test message");
                received = true;
                break;
            }
        });

        await Task.Delay(250);

        var testEvent = new TestEvent
        {
            SagaId = Guid.NewGuid(),
            Message = "Integration test message"
        };
        await eventBus.Publish(testEvent);

        await consumeTask;

        received.Should().BeTrue();

        await eventBus.DisposeAsync();
    }

    [Fact]
    public async Task PublishThenConsume_Event_MultiConsumer_Succeeds()
    {
        //string amqpUri = "amqp://guest:guest@localhost:5672";
        var amqpUri = _rabbitMqContainer.GetConnectionString();
        var applicationId = "TestApp";
        var handlerType1 = typeof(TestEventHandlerA);
        var handlerType2 = typeof(TestEventHandlerB);

        // Separate queueTypeMap entry for each handler
        var queueTypeMap = new Dictionary<string, (Type, Type)>
        {
            { MessagingNamingHelper.GetRoutingKey(typeof(TestEvent), handlerType1, applicationId), (typeof(TestEvent), typeof(TestEventHandlerA)) },
            { MessagingNamingHelper.GetRoutingKey(typeof(TestEvent), handlerType2, applicationId), (typeof(TestEvent), typeof(TestEventHandlerB)) }
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

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        int receivedCount = 0;

        var consumeTask = Task.Run(async () =>
        {
            await foreach (var (body, messageType, _) in eventBus.ConsumeAsync(cancellationToken: cts.Token))
            {
                var json = Encoding.UTF8.GetString(body);
                var evt = System.Text.Json.JsonSerializer.Deserialize(json, messageType);
                evt.Should().BeOfType<TestEvent>();
                ((TestEvent)evt).Message.Should().Be("Integration test message multi");

                receivedCount++;
                if (receivedCount >= 2) // Exit if both handlers have received the message
                    break;
            }
        });

        await Task.Delay(250);

        var testEvent = new TestEvent
        {
            SagaId = Guid.NewGuid(),
            Message = "Integration test message multi"
        };
        // Publish is done only once, but both handlers receive it from different queues
        await eventBus.Publish(testEvent); // Here, handlerType is only important for publish to determine the exchange
        // You can send handlerType1 or handlerType2 in the above line, it doesn't matter.

        await consumeTask;

        receivedCount.Should().Be(2);

        await eventBus.DisposeAsync();
    }

    [Fact]
    public async Task SendThenConsume_Command_Succeeds()
    {
        //string amqpUri = "amqp://guest:guest@localhost:5672";
        var amqpUri = _rabbitMqContainer.GetConnectionString();
        var applicationId = "TestApp";
        var handlerType = typeof(TestCommandHandlerA);

        // Only a single consumer/queue mapping for command (point-to-point)
        var queueTypeMap = new Dictionary<string, (Type, Type)>
        {
            {
                MessagingNamingHelper.GetRoutingKey(typeof(TestCommand), handlerType, applicationId),
                (typeof(TestCommand), typeof(TestCommandHandlerA))
            }
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

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        bool received = false;

        var consumeTask = Task.Run(async () =>
        {
            await foreach (var (body, messageType, _) in eventBus.ConsumeAsync(cancellationToken: cts.Token))
            {
                var json = Encoding.UTF8.GetString(body);
                var cmd = System.Text.Json.JsonSerializer.Deserialize(json, messageType);

                cmd.Should().BeOfType<TestCommand>();
                ((TestCommand)cmd).Message.Should().Be("Integration test command");
                received = true;
                break;
            }
        });

        await Task.Delay(250);

        var testCommand = new TestCommand
        {
            SagaId = Guid.NewGuid(),
            Message = "Integration test command"
        };
        await eventBus.Send(testCommand);

        await consumeTask;

        received.Should().BeTrue();

        await eventBus.DisposeAsync();
    }

// Dummy command handler for test
    private class TestCommandHandlerA : StartReactiveSagaHandler<TestCommand>
    {
        public override Task HandleStartAsync(TestCommand message, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

// Test command for Send
    private class TestCommand : CommandBase
    {
        public string Message { get; set; } = string.Empty;
    }

    private class TestEventHandlerA : StartReactiveSagaHandler<TestEvent>
    {
        public override Task HandleStartAsync(TestEvent message, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private class TestEventHandlerB : StartReactiveSagaHandler<TestEvent>
    {
        public override Task HandleStartAsync(TestEvent message, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }


    private class TestEvent : EventBase
    {
        public string Message { get; set; } = string.Empty;
    }
}