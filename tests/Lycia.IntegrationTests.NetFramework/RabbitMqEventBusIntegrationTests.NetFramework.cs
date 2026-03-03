// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using System.Text;
using FluentAssertions;
using Lycia.Extensions.Configurations;
using Lycia.Extensions.Eventing;
using Lycia.Extensions.Serialization;
using Lycia.Helpers;
using Lycia.Saga.Messaging;
using Lycia.Saga.Messaging.Handlers;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using Testcontainers.RabbitMq;

namespace Lycia.IntegrationTests;

public class RabbitMqEventBusIntegrationTestsNetFramework : IAsyncLifetime
{
    // Use the official RabbitMqContainer builder
    private readonly RabbitMqContainer _rabbitMqContainer =
        new RabbitMqBuilder()
            .WithImage("rabbitmq:3.13-management-alpine")
            .WithCleanUp(true)
            .Build();
    
    private string RabbitMqConnectionString =>
        //"amqp://guest:guest@127.0.0.1:5672/"; 
        _rabbitMqContainer.GetConnectionString();

    private static async Task CleanupQueuesAsync(string connectionString, string queueName)
    {
        var factory = new ConnectionFactory { Uri = new Uri(connectionString) };
        using var conn = factory.CreateConnection();
        using var ch = conn.CreateModel();
        try { ch.QueueDelete(queueName); } catch { /* ignore */ }
        try { ch.QueueDelete(queueName + ".dlq"); } catch { /* ignore */ }
        await Task.CompletedTask;
    }

    public async Task InitializeAsync()
        => await _rabbitMqContainer.StartAsync().ConfigureAwait(false);

    public async Task DisposeAsync()
        => await _rabbitMqContainer.DisposeAsync().ConfigureAwait(false);
    
    
    [Fact]
    public async Task Publish_Event_Expires_To_DLQ_Succeeds()
    {
        var applicationId = "TestApp";
        var handlerType = typeof(TestEventHandlerA);
        var queueName = MessagingNamingHelper.GetRoutingKey(typeof(TestEvent), handlerType, applicationId);

        // Clean up before test (best practice for integration tests)
        var factory = new ConnectionFactory { Uri = new Uri(RabbitMqConnectionString) };
        using (var conn = factory.CreateConnection())
        using (var channelDelete = conn.CreateModel())
        {
            try
            {
                channelDelete.QueueDelete(queueName);
            }
            catch
            {
                // ignored
            }

            try
            {
                channelDelete.QueueDelete(queueName + ".dlq");
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
            MessageTTL = ttl,
            ConnectionString = RabbitMqConnectionString
        };

        var serializer = new NewtonsoftJsonMessageSerializer();

        await using (var consumerBus = await RabbitMqEventBus.CreateAsync(
                         NullLogger<RabbitMqEventBus>.Instance,
                         queueTypeMap,
                         eventBusOptions,
                         serializer))
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)); // Extra time for test

            try
            {
                // Trigger consumer (do not process any messages)
                // Start the consumer just to trigger queue creation, don't wait for any message
                await using var enumerator = consumerBus.ConsumeAsync(autoAck: false, cancellationToken: cts.Token)
                    .GetAsyncEnumerator(cts.Token);
                // Just trigger queue/DLQ creation (no actual message expected)
                await enumerator.MoveNextAsync();
            }
            catch (TaskCanceledException)
            {
                /* Ignore cancellation*/
            }
            catch (OperationCanceledException)
            {
                /* Ignore cancellation */
            }
        }

        await Task.Delay(500);

        await using (var publisherBus = await RabbitMqEventBus.CreateAsync(
                         NullLogger<RabbitMqEventBus>.Instance,
                         queueTypeMap,
                         eventBusOptions,
                         serializer))
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

        using var conn2 = factory.CreateConnection();
        using var channel = conn2.CreateModel();

        var dlqName = queueName + ".dlq";

        var result = channel.QueueDeclarePassive(dlqName);
        result.MessageCount.Should().Be(1, "The message should be dead-lettered after TTL expires.");

        var dlqResult = channel.BasicGet(dlqName, autoAck: true);
        dlqResult.Should().NotBeNull();

        var body = Encoding.UTF8.GetString(dlqResult.Body.ToArray());
        body.Should().Contain("DLQ Test Message");
    }

    [Fact]
    public async Task PublishThenConsume_WithAck_MessageNotInDlq()
    {
        var applicationId = "TestApp";
        var handlerType = typeof(TestEventHandlerA);
        var queueName = MessagingNamingHelper.GetRoutingKey(typeof(TestEvent), handlerType, applicationId);

        await CleanupQueuesAsync(RabbitMqConnectionString, queueName);

        var queueTypeMap = new Dictionary<string, (Type, Type)>
        {
            { queueName, (typeof(TestEvent), typeof(TestEventHandlerA)) }
        };

        var eventBusOptions = new EventBusOptions
        {
            ApplicationId = applicationId,
            MessageTTL = TimeSpan.FromMinutes(1),
            ConnectionString = RabbitMqConnectionString
        };

        var serializer = new NewtonsoftJsonMessageSerializer();

        await using var bus = await RabbitMqEventBus.CreateAsync(
            NullLogger<RabbitMqEventBus>.Instance,
            queueTypeMap,
            eventBusOptions,
            serializer);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        // Start consuming with manual ack/nack to trigger declarations
        var consumeTask = Task.Run(async () =>
        {
            await foreach (var msg in bus.ConsumeWithAckAsync(cts.Token))
            {
                // Simulate successful dispatch
                await msg.Ack();
                break;
            }
        }, cts.Token);

        await Task.Delay(200, cts.Token);

        var evt = new TestEvent { SagaId = Guid.NewGuid(), Message = "Ack path message" };
        await bus.Publish(evt);

        await consumeTask;

        // Assert DLQ is empty
        var factory = new ConnectionFactory { Uri = new Uri(RabbitMqConnectionString) };
        using var conn = factory.CreateConnection();
        using var ch = conn.CreateModel();
        var dlqName = queueName + ".dlq";

        try
        {
            var passive = ch.QueueDeclarePassive(dlqName);
            passive.MessageCount.Should().Be(0, "Acked message must not appear in DLQ");
        }
        catch (RabbitMQ.Client.Exceptions.OperationInterruptedException)
        {
            // DLQ not created at all ⇒ also acceptable as "empty"
            true.Should().BeTrue();
        }
    }

    [Fact]
    public async Task PublishThenConsume_WithNackFalse_MessageGoesToDlq()
    {
        var applicationId = "TestApp";
        var handlerType = typeof(TestEventHandlerA);
        var queueName = MessagingNamingHelper.GetRoutingKey(typeof(TestEvent), handlerType, applicationId);

        await CleanupQueuesAsync(RabbitMqConnectionString, queueName);

        var queueTypeMap = new Dictionary<string, (Type, Type)>
        {
            { queueName, (typeof(TestEvent), typeof(TestEventHandlerA)) }
        };

        var eventBusOptions = new EventBusOptions
        {
            ApplicationId = applicationId,
            MessageTTL = TimeSpan.FromMinutes(1),
            ConnectionString = RabbitMqConnectionString
        };

        var serializer = new NewtonsoftJsonMessageSerializer();

        await using var bus = await RabbitMqEventBus.CreateAsync(
            NullLogger<RabbitMqEventBus>.Instance,
            queueTypeMap,
            eventBusOptions,
            serializer);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var consumeTask = Task.Run(async () =>
        {
            await foreach (var msg in bus.ConsumeWithAckAsync(cts.Token))
            {
                // Simulate failed dispatch after in-process retries are exhausted
                await msg.Nack(false);
                break;
            }
        }, cts.Token);

        await Task.Delay(200, cts.Token);

        var evt = new TestEvent { SagaId = Guid.NewGuid(), Message = "Nack path message" };
        await bus.Publish(evt);

        await consumeTask;

        // Assert DLQ contains the message
        var factory = new ConnectionFactory { Uri = new Uri(RabbitMqConnectionString) };
        using var conn = factory.CreateConnection();
        using var ch = conn.CreateModel();
        var dlqName = queueName + ".dlq";

        var passive = ch.QueueDeclarePassive(dlqName);
        passive.MessageCount.Should().Be(1, "Nack(false) must route the message to DLQ");

        var dlqResult = ch.BasicGet(dlqName, autoAck: true);
        dlqResult.Should().NotBeNull();
        var body = Encoding.UTF8.GetString(dlqResult.Body.ToArray());
        body.Should().Contain("Nack path message");
    }

    [Fact]
    public async Task PublishThenConsume_Event_Succeeds()
    {
        var applicationId = "TestApp";
        var handlerType = typeof(TestEventHandlerA);

        var queueTypeMap = new Dictionary<string, (Type, Type)>
        {
            {
                MessagingNamingHelper.GetRoutingKey(typeof(TestEvent), handlerType, applicationId),
                (typeof(TestEvent), typeof(TestEventHandlerA))
            }
        };

        var eventBusOptions = new EventBusOptions
        {
            ApplicationId = applicationId,
            MessageTTL = TimeSpan.FromMinutes(5),
            ConnectionString = RabbitMqConnectionString
        };

        var serializer = new NewtonsoftJsonMessageSerializer();

        var eventBus = await RabbitMqEventBus.CreateAsync(
            NullLogger<RabbitMqEventBus>.Instance,
            queueTypeMap,
            eventBusOptions,
            serializer);

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        bool received = false;

        var consumeTask = Task.Run(async () =>
        {
            await foreach (var (body, messageType, handlerType, headers) in eventBus.ConsumeAsync(cancellationToken: cts.Token))
            {
                var normalizedHeaders = serializer.NormalizeTransportHeaders(headers);
                var (_, ctx) = serializer.CreateContextFor(messageType);
                var evt = serializer.Deserialize(body, normalizedHeaders, ctx);

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
        var applicationId = "TestApp";
        var handlerType1 = typeof(TestEventHandlerA);
        var handlerType2 = typeof(TestEventHandlerB);

        // Separate queueTypeMap entry for each handler
        var queueTypeMap = new Dictionary<string, (Type, Type)>
        {
            {
                MessagingNamingHelper.GetRoutingKey(typeof(TestEvent), handlerType1, applicationId),
                (typeof(TestEvent), typeof(TestEventHandlerA))
            },
            {
                MessagingNamingHelper.GetRoutingKey(typeof(TestEvent), handlerType2, applicationId),
                (typeof(TestEvent), typeof(TestEventHandlerB))
            }
        };

        var eventBusOptions = new EventBusOptions
        {
            ApplicationId = applicationId,
            MessageTTL = TimeSpan.FromMinutes(5),
            ConnectionString = RabbitMqConnectionString
        };

        var serializer = new NewtonsoftJsonMessageSerializer();

        var eventBus = await RabbitMqEventBus.CreateAsync(
            NullLogger<RabbitMqEventBus>.Instance,
            queueTypeMap,
            eventBusOptions,
            serializer);

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        int receivedCount = 0;

        var consumeTask = Task.Run(async () =>
        {
            await foreach (var (body, messageType, handlerType, headers) in eventBus.ConsumeAsync(cancellationToken: cts.Token))
            {
                var normalizedHeaders = serializer.NormalizeTransportHeaders(headers);
                var (_, ctx) = serializer.CreateContextFor(messageType);
                var evt = serializer.Deserialize(body, normalizedHeaders, ctx);
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
            MessageTTL = TimeSpan.FromMinutes(5),
            ConnectionString = RabbitMqConnectionString
        };

        var serializer = new NewtonsoftJsonMessageSerializer();

        var eventBus = await RabbitMqEventBus.CreateAsync(
            NullLogger<RabbitMqEventBus>.Instance,
            queueTypeMap,
            eventBusOptions,
            serializer);

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        bool received = false;

        var consumeTask = Task.Run(async () =>
        {
            await foreach (var (body, messageType, handlerType, headers) in eventBus.ConsumeAsync(cancellationToken: cts.Token))
            {
                var normalizedHeaders = serializer.NormalizeTransportHeaders(headers);
                var (_, ctx) = serializer.CreateContextFor(messageType);
                var cmd = serializer.Deserialize(body, normalizedHeaders, ctx);

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
        public override Task HandleStartAsync(TestCommand message, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

// Test command for Send
    private class TestCommand : CommandBase
    {
        public string Message { get; set; } = string.Empty;
    }

    private class TestEventHandlerA : StartReactiveSagaHandler<TestEvent>
    {
        public override Task HandleStartAsync(TestEvent message, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private class TestEventHandlerB : StartReactiveSagaHandler<TestEvent>
    {
        public override Task HandleStartAsync(TestEvent message, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private class TestEvent : EventBase
    {
        public string Message { get; set; } = string.Empty;
    }
}
