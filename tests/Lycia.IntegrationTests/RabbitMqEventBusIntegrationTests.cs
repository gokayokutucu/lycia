// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using System.Text;
using FluentAssertions;
using Lycia.Extensions.Configurations;
using Lycia.Extensions.Eventing;
using Lycia.Extensions.Serialization;

using Lycia.Helpers;
using Lycia.Saga.Abstractions.Messaging;
using Lycia.Saga.Abstractions.Scheduling;
using Lycia.Saga.Messaging;
using Lycia.Saga.Messaging.Handlers;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Testcontainers.RabbitMq;

namespace Lycia.IntegrationTests;

public class RabbitMqEventBusIntegrationTests : IAsyncLifetime
{
    private readonly RabbitMqContainer? _rabbitMqContainer;
    private readonly string? _externalConnectionString;

    public RabbitMqEventBusIntegrationTests()
    {
        _externalConnectionString = Environment.GetEnvironmentVariable("LYCIA_RABBITMQ_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(_externalConnectionString))
            _rabbitMqContainer = new RabbitMqBuilder()
                .WithImage("rabbitmq:3-management")
                .WithCleanUp(true)
                .Build();
    }
    
    private string RabbitMqConnectionString =>
        _externalConnectionString ?? _rabbitMqContainer?.GetConnectionString()
        ?? throw new InvalidOperationException("RabbitMQ test fixture is not configured.");

    private static async Task CleanupQueuesAsync(string connectionString, string queueName)
    {
        var factory = new ConnectionFactory { Uri = new Uri(connectionString) };
        await using var conn = await factory.CreateConnectionAsync(CancellationToken.None);
        await using var ch = await conn.CreateChannelAsync(cancellationToken: CancellationToken.None);
        try { await ch.QueueDeleteAsync(queueName); } catch { /* ignore */ }
        try { await ch.QueueDeleteAsync(queueName + ".dlq"); } catch { /* ignore */ }
    }

    public async Task InitializeAsync()
    {
        if (_rabbitMqContainer != null)
            await _rabbitMqContainer.StartAsync().ConfigureAwait(false);
    }

    public async Task DisposeAsync()
    {
        if (_rabbitMqContainer != null)
            await _rabbitMqContainer.DisposeAsync().ConfigureAwait(false);
    }
    
    
    [Fact]
    public async Task Publish_Event_Expires_To_DLQ_Succeeds()
    {

        var applicationId = "TestApp";
        var handlerType = typeof(TestEventHandlerA);
        var queueName = MessagingNamingHelper.GetQueueName(typeof(TestEvent), handlerType, applicationId);

        // Clean up before test (best practice for integration tests)
        var factory = new ConnectionFactory { Uri = new Uri(RabbitMqConnectionString) };
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
    public async Task PublishThenConsume_WithAck_MessageNotInDlq()
    {
        var applicationId = "TestApp";
        var handlerType = typeof(TestEventHandlerA);
        var queueName = MessagingNamingHelper.GetQueueName(typeof(TestEvent), handlerType, applicationId);

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
        await using var conn = await factory.CreateConnectionAsync(CancellationToken.None);
        await using var ch = await conn.CreateChannelAsync(cancellationToken: CancellationToken.None);
        var dlqName = queueName + ".dlq";

        try
        {
            var passive = await ch.QueueDeclarePassiveAsync(dlqName, CancellationToken.None);
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
        var queueName = MessagingNamingHelper.GetQueueName(typeof(TestEvent), handlerType, applicationId);

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
        await using var conn = await factory.CreateConnectionAsync(CancellationToken.None);
        await using var ch = await conn.CreateChannelAsync(cancellationToken: CancellationToken.None);
        var dlqName = queueName + ".dlq";

        var passive = await ch.QueueDeclarePassiveAsync(dlqName, CancellationToken.None);
        passive.MessageCount.Should().Be(1, "Nack(false) must route the message to DLQ");

        var dlqResult = await ch.BasicGetAsync(dlqName, autoAck: true, cancellationToken: CancellationToken.None);
        dlqResult.Should().NotBeNull();
        var body = Encoding.UTF8.GetString(dlqResult.Body.ToArray());
        body.Should().Contain("Nack path message");
    }

    [Fact]
    public async Task TargetedResponse_NackToDlq_PreservesRequestAndCausationMetadata()
    {
        var applicationId = $"Response-Dlq-{Guid.NewGuid():N}";
        var queueName = MessagingNamingHelper.GetResponseQueueName(typeof(TestResponse), applicationId);
        await CleanupQueuesAsync(RabbitMqConnectionString, queueName);
        var serializer = new NewtonsoftJsonMessageSerializer();
        var queueTypeMap = new Dictionary<string, (Type, Type)>
        {
            [queueName] = (typeof(TestResponse), typeof(TestCommandHandlerA))
        };
        var options = new EventBusOptions
        {
            ApplicationId = applicationId,
            MessageTTL = TimeSpan.FromMinutes(1),
            ConnectionString = RabbitMqConnectionString
        };
        await using var bus = await RabbitMqEventBus.CreateAsync(
            NullLogger<RabbitMqEventBus>.Instance, queueTypeMap, options, serializer);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var consume = Task.Run(async () =>
        {
            await foreach (var incoming in bus.ConsumeWithAckAsync(timeout.Token))
            {
                await incoming.Nack(false);
                return;
            }
        }, timeout.Token);

        await Task.Delay(250, timeout.Token);
        var sagaId = Guid.NewGuid();
        var workflowId = Guid.NewGuid();
        var request = new TestCommand
        {
            SagaId = sagaId,
            CorrelationId = workflowId,
            ResponseEndpoint = applicationId,
            Message = "request-for-dlq"
        };
        request.RequestId = request.MessageId;
        await bus.Respond(
            request,
            new TestResponse { Message = "response-for-dlq" },
            cancellationToken: timeout.Token);
        await consume.WaitAsync(timeout.Token);

        var factory = new ConnectionFactory { Uri = new Uri(RabbitMqConnectionString) };
        await using var connection = await factory.CreateConnectionAsync(CancellationToken.None);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: CancellationToken.None);
        var dlqResult = await channel.BasicGetAsync(
            queueName + ".dlq", autoAck: true, cancellationToken: CancellationToken.None);
        dlqResult.Should().NotBeNull();
        var normalized = serializer.NormalizeTransportHeaders(
            new Dictionary<string, object?>(
                dlqResult.BasicProperties.Headers ?? new Dictionary<string, object?>()));
        var (_, context) = serializer.CreateContextFor(typeof(TestResponse));
        var response = (TestResponse)serializer.Deserialize(dlqResult.Body.ToArray(), normalized, context);

        response.Message.Should().Be("response-for-dlq");
        response.MessageId.Should().NotBe(request.MessageId);
        response.RequestId.Should().Be(request.MessageId);
        response.CorrelationId.Should().Be(workflowId);
        response.CausationId.Should().Be(request.MessageId);
        response.ParentMessageId.Should().Be(request.MessageId);
        response.SagaId.Should().Be(sagaId);
        response.ResponseEndpoint.Should().Be(
            Lycia.Messaging.EndpointIdentityNormalizer.Default.Normalize(applicationId));
    }

    [Fact]
    public async Task PublishThenConsume_Event_Succeeds()
    {
        var applicationId = "EventSingle" + Guid.NewGuid().ToString("N");
        var handlerType = typeof(TestEventHandlerA);
        var queueName = MessagingNamingHelper.GetQueueName(typeof(TestEvent), handlerType, applicationId);

        var queueTypeMap = new Dictionary<string, (Type, Type)>
        {
            {
                queueName,
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
        await CleanupQueuesAsync(RabbitMqConnectionString, queueName);
    }

    [Fact]
    public async Task PublishThenConsume_Event_MultiConsumer_Succeeds()
    {
        var applicationId = "EventMulti" + Guid.NewGuid().ToString("N");
        var handlerType1 = typeof(TestEventHandlerA);
        var handlerType2 = typeof(TestEventHandlerB);
        var queueName1 = MessagingNamingHelper.GetQueueName(typeof(TestEvent), handlerType1, applicationId);
        var queueName2 = MessagingNamingHelper.GetQueueName(typeof(TestEvent), handlerType2, applicationId);

        // Separate queueTypeMap entry for each handler
        var queueTypeMap = new Dictionary<string, (Type, Type)>
        {
            {
                queueName1,
                (typeof(TestEvent), typeof(TestEventHandlerA))
            },
            {
                queueName2,
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
        await CleanupQueuesAsync(RabbitMqConnectionString, queueName1);
        await CleanupQueuesAsync(RabbitMqConnectionString, queueName2);
    }

    [Fact]
    public async Task SendThenConsume_Command_Succeeds()
    {
        var applicationId = "TestApp";
        var handlerType = typeof(TestCommandHandlerA);
        var queueName = MessagingNamingHelper.GetQueueName(typeof(TestCommand), handlerType, applicationId);
        await CleanupQueuesAsync(RabbitMqConnectionString, queueName);

        // Only a single consumer/queue mapping for command (point-to-point)
        var queueTypeMap = new Dictionary<string, (Type, Type)>
        {
            {
                queueName,
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
        await CleanupQueuesAsync(RabbitMqConnectionString, queueName);
    }

    [Fact]
    public async Task Respond_TargetsCanonicalEndpoint_AndPreservesIdentityMetadata()
    {
        var applicationId = "Test-App";
        var queueName = MessagingNamingHelper.GetResponseQueueName(typeof(TestResponse), applicationId);
        var queueTypeMap = new Dictionary<string, (Type, Type)>
        {
            [queueName] = (typeof(TestResponse), typeof(TestCommandHandlerA))
        };
        var consumerOptions = new EventBusOptions
        {
            ApplicationId = applicationId,
            MessageTTL = TimeSpan.FromMinutes(1),
            ConnectionString = RabbitMqConnectionString
        };
        var serializer = new NewtonsoftJsonMessageSerializer();
        await using var consumerBus = await RabbitMqEventBus.CreateAsync(
            NullLogger<RabbitMqEventBus>.Instance, queueTypeMap, consumerOptions, serializer);
        var producerOptions = new EventBusOptions
        {
            ApplicationId = "test_app",
            MessageTTL = TimeSpan.FromMinutes(1),
            ConnectionString = RabbitMqConnectionString
        };
        await using var producerBus = await RabbitMqEventBus.CreateAsync(
            NullLogger<RabbitMqEventBus>.Instance,
            new Dictionary<string, (Type, Type)>(), producerOptions, serializer);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var receive = Task.Run(async () =>
        {
            await foreach (var incoming in consumerBus.ConsumeWithAckAsync(timeout.Token))
            {
                await incoming.Ack();
                var normalized = serializer.NormalizeTransportHeaders(incoming.Headers);
                var (_, context) = serializer.CreateContextFor(typeof(TestResponse));
                return (TestResponse)serializer.Deserialize(incoming.Body, normalized, context);
            }
            throw new InvalidOperationException("RabbitMQ response consumer completed early.");
        }, timeout.Token);

        await Task.Delay(300, timeout.Token);
        var request = new TestCommand { SagaId = Guid.NewGuid(), Message = "request" };
        await producerBus.Send(request, cancellationToken: timeout.Token);
        var response = new TestResponse { Message = "response" };
        await producerBus.Respond(request, response, cancellationToken: timeout.Token);

        var received = await receive.WaitAsync(timeout.Token);
        received.RequestId.Should().Be(request.MessageId);
        received.MessageId.Should().NotBe(request.MessageId);
        received.CausationId.Should().Be(request.MessageId);
        received.ParentMessageId.Should().Be(request.MessageId);
        received.ResponseEndpoint.Should().Be("testapp");
    }

    [Fact]
    public async Task Native_predefined_schedule_uses_fixed_ttl_and_dead_letters_to_the_final_exchange()
    {
        var applicationId = "ScheduleTest" + Guid.NewGuid().ToString("N");
        var options = new EventBusOptions
        {
            ApplicationId = applicationId,
            ConnectionString = RabbitMqConnectionString
        };
        var serializer = new NewtonsoftJsonMessageSerializer();
        await using var bus = await RabbitMqEventBus.CreateAsync(
            NullLogger<RabbitMqEventBus>.Instance, new Dictionary<string, (Type, Type)>(), options, serializer);
        var finalExchange = MessagingNamingHelper.GetExchangeName(typeof(TestEvent));
        var finalQueue = "lycia.schedule.integration." + Guid.NewGuid().ToString("N");
        var factory = new ConnectionFactory { Uri = new Uri(RabbitMqConnectionString) };
        await using var connection = await factory.CreateConnectionAsync(CancellationToken.None);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: CancellationToken.None);
        await channel.ExchangeDeclareAsync(finalExchange, ExchangeType.Fanout, durable: true, autoDelete: false);
        await channel.QueueDeclareAsync(finalQueue, durable: false, exclusive: false, autoDelete: true);
        await channel.QueueBindAsync(finalQueue, finalExchange, string.Empty);
        var record = new ScheduleRecord
        {
            ScheduleId = Guid.NewGuid(),
            MessageId = Guid.NewGuid(),
            MessageType = typeof(TestEvent).AssemblyQualifiedName!,
            MessageKind = ScheduledMessageKind.Event,
            Destination = applicationId,
            DueAtUtc = DateTimeOffset.UtcNow.AddSeconds(5),
            ScheduledAtUtc = DateTimeOffset.UtcNow,
            Status = ScheduleStatus.Pending,
            Payload = [1, 2, 3],
            IsPredefined = true,
            DelaySuffix = "5s",
            IdempotencyKey = "delay:5s"
        };

        var resource = await bus.ScheduleNativeAsync(new NativeScheduleEnvelope
        {
            Record = record,
            Delay = TimeSpan.FromSeconds(5)
        });

        resource.Should().NotBeNullOrWhiteSpace();
        _ = await channel.QueueDeclarePassiveAsync(resource!, CancellationToken.None);
        BasicGetResult? delivered = null;
        var deliveryDeadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (delivered == null && DateTimeOffset.UtcNow < deliveryDeadline)
        {
            delivered = await channel.BasicGetAsync(finalQueue, autoAck: true, cancellationToken: CancellationToken.None);
            if (delivered == null) await Task.Delay(250);
        }
        delivered.Should().NotBeNull();
        delivered!.Body.ToArray().Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task Native_dynamic_schedule_delivers_no_earlier_than_due_preserves_metadata_and_vacuums_conditionally()
    {
        var applicationId = "DynamicScheduleTest" + Guid.NewGuid().ToString("N");
        var options = new EventBusOptions
        {
            ApplicationId = applicationId,
            ConnectionString = RabbitMqConnectionString
        };
        var serializer = new NewtonsoftJsonMessageSerializer();
        await using var bus = await RabbitMqEventBus.CreateAsync(
            NullLogger<RabbitMqEventBus>.Instance, new Dictionary<string, (Type, Type)>(), options, serializer);
        var finalExchange = MessagingNamingHelper.GetExchangeName(typeof(TestEvent));
        var finalQueue = "lycia.schedule.dynamic.integration." + Guid.NewGuid().ToString("N");
        var factory = new ConnectionFactory { Uri = new Uri(RabbitMqConnectionString) };
        await using var connection = await factory.CreateConnectionAsync(CancellationToken.None);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: CancellationToken.None);
        await channel.ExchangeDeclareAsync(finalExchange, ExchangeType.Fanout, durable: true, autoDelete: false);
        await channel.QueueDeclareAsync(finalQueue, durable: false, exclusive: false, autoDelete: true);
        await channel.QueueBindAsync(finalQueue, finalExchange, string.Empty);
        var messageId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var causationId = Guid.NewGuid();
        var sagaId = Guid.NewGuid();
        var record = new ScheduleRecord
        {
            ScheduleId = Guid.NewGuid(),
            MessageId = messageId,
            CorrelationId = correlationId,
            CausationId = causationId,
            ParentMessageId = causationId,
            SagaId = sagaId,
            MessageType = typeof(TestEvent).AssemblyQualifiedName!,
            MessageKind = ScheduledMessageKind.Event,
            Destination = applicationId,
            DueAtUtc = DateTimeOffset.UtcNow.AddSeconds(2),
            ScheduledAtUtc = DateTimeOffset.UtcNow,
            Status = ScheduleStatus.Pending,
            Payload = [4, 5, 6],
            Headers = new Dictionary<string, object?>
            {
                ["MessageId"] = messageId.ToString(),
                ["CorrelationId"] = correlationId.ToString(),
                ["CausationId"] = causationId.ToString(),
                ["ParentMessageId"] = causationId.ToString(),
                ["SagaId"] = sagaId.ToString()
            },
            IsPredefined = false,
            DelaySuffix = "2000ms",
            IdempotencyKey = "delay:2000ms"
        };

        var resourceName = await bus.ScheduleNativeAsync(new NativeScheduleEnvelope
        {
            Record = record,
            Delay = TimeSpan.FromSeconds(2)
        });
        resourceName.Should().NotBeNullOrWhiteSpace();
        var resource = new SchedulingResourceRecord
        {
            ResourceId = resourceName!,
            CanonicalName = resourceName!,
            Transport = "rabbitmq",
            ResourceType = "queue",
            ManagementMode = SchedulingResourceManagementMode.DynamicScheduling,
            IsDynamic = true
        };

        (await bus.DeleteConditionallyAsync(resource)).Should().BeFalse("the delay queue still contains a message");
        await Task.Delay(500);
        (await channel.BasicGetAsync(finalQueue, autoAck: true, cancellationToken: CancellationToken.None))
            .Should().BeNull("RabbitMQ must not deliver a scheduled message before its TTL");

        BasicGetResult? delivered = null;
        var deliveryDeadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (delivered == null && DateTimeOffset.UtcNow < deliveryDeadline)
        {
            delivered = await channel.BasicGetAsync(finalQueue, autoAck: true, cancellationToken: CancellationToken.None);
            if (delivered == null) await Task.Delay(100);
        }
        delivered.Should().NotBeNull();
        delivered!.Body.ToArray().Should().Equal(4, 5, 6);
        delivered.BasicProperties.MessageId.Should().Be(messageId.ToString("D"));
        HeaderText(delivered, "CorrelationId").Should().Be(correlationId.ToString());
        HeaderText(delivered, "CausationId").Should().Be(causationId.ToString());
        HeaderText(delivered, "ParentMessageId").Should().Be(causationId.ToString());
        HeaderText(delivered, "SagaId").Should().Be(sagaId.ToString());

        await using var manager = await RabbitMqEventBus.CreateAsync(
            NullLogger<RabbitMqEventBus>.Instance, new Dictionary<string, (Type, Type)>(), options, serializer);
        var state = await manager.InspectAsync(resource);
        state.Exists.Should().BeTrue();
        state.MessageCount.Should().Be(0);
        state.ConsumerCount.Should().Be(0);
        state.OwnershipProven.Should().BeTrue();

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, _) => Task.CompletedTask;
        var consumerTag = await channel.BasicConsumeAsync(resourceName!, autoAck: true, consumer);
        (await manager.DeleteConditionallyAsync(resource)).Should().BeFalse("the queue has an active consumer");
        await channel.BasicCancelAsync(consumerTag);

        await using var finalManager = await RabbitMqEventBus.CreateAsync(
            NullLogger<RabbitMqEventBus>.Instance, new Dictionary<string, (Type, Type)>(), options, serializer);
        (await finalManager.DeleteConditionallyAsync(resource)).Should().BeTrue();
        (await finalManager.InspectAsync(resource)).Exists.Should().BeFalse();
    }

    private static string HeaderText(BasicGetResult delivery, string name)
    {
        delivery.BasicProperties.Headers.Should().ContainKey(name);
        return Encoding.UTF8.GetString((byte[])delivery.BasicProperties.Headers![name]!);
    }

// Dummy command handler for test
    private class TestCommandHandlerA : StartReactiveSagaHandler<TestCommand>
    {
        public override Task HandleStartAsync(TestCommand message, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

// Test command for Send
    private interface ITestAppCommand : ICommand, ICommandEndpoint { }

    private class TestCommand : CommandBase, ITestAppCommand
    {
        public string Message { get; set; } = string.Empty;
    }

    private sealed class TestResponse : ResponseBase<TestCommand>
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
