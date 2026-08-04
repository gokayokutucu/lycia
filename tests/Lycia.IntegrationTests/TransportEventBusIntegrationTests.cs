using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FluentAssertions;
using Lycia.Common.Messaging;
using Lycia.Extensions.Kafka;
using Lycia.Extensions.Nats;
using Lycia.Extensions.Serialization;
using Lycia.Helpers;
using Lycia.Saga.Abstractions.Messaging;
using Lycia.Saga.Messaging;
using Testcontainers.Kafka;

namespace Lycia.IntegrationTests;

public sealed class NatsEventBusIntegrationTests : IAsyncLifetime
{
    private readonly IContainer _container = new ContainerBuilder()
        .WithImage("nats:2.11-alpine")
        .WithCommand("-js")
        .WithPortBinding(4222, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Server is ready"))
        .WithCleanUp(true)
        .Build();

    public Task InitializeAsync() => _container.StartAsync();
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    [Fact]
    public async Task JetStream_preserves_command_ownership_and_independent_event_subscriptions()
    {
        var stream = $"LYCIA_{Guid.NewGuid():N}";
        var ownerOptions = new NatsEventBusOptions
        {
            Url = $"nats://{_container.Hostname}:{_container.GetMappedPublicPort(4222)}",
            ApplicationId = "OwnerService",
            StreamName = stream
        };
        var senderOptions = new NatsEventBusOptions
        {
            Url = ownerOptions.Url,
            ApplicationId = "RequesterService",
            StreamName = stream
        };
        var queue = MessagingNamingHelper.GetQueueName(
            typeof(OwnedCommand), typeof(OwnedCommandHandler), ownerOptions.ApplicationId);
        var map = new Dictionary<string, (Type, Type)>
        {
            [queue] = (typeof(OwnedCommand), typeof(OwnedCommandHandler))
        };
        var serializer = new NewtonsoftJsonMessageSerializer();

        await using var consumer = new NatsEventBus(map, ownerOptions, serializer);
        await using var publisher = new NatsEventBus(
            new Dictionary<string, (Type, Type)>(), senderOptions, serializer);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));

        var receive = ReceiveOneAsync(consumer, timeout.Token);
        await Task.Delay(500, timeout.Token);
        var command = new OwnedCommand { Value = "nats" };
        await publisher.Send(command, cancellationToken: timeout.Token);

        var incoming = await receive;
        incoming.MessageType.Should().Be(typeof(OwnedCommand));
        incoming.Headers["ReplyTo"].Should().Be("RequesterService");

        var eventMap = CreateEventMap(ownerOptions.ApplicationId);
        await using var eventConsumer = new NatsEventBus(eventMap, ownerOptions, serializer);
        var receiveEvents = ReceiveEventsAsync(eventConsumer, 2, timeout.Token);
        await Task.Delay(500, timeout.Token);
        await publisher.Publish(new OwnedEvent { Value = "nats-event" }, cancellationToken: timeout.Token);
        var eventHandlers = (await receiveEvents).Select(message => message.HandlerType).ToArray();
        eventHandlers.Should().Contain(typeof(OwnedEventHandlerA));
        eventHandlers.Should().Contain(typeof(OwnedEventHandlerB));
    }

    private static async Task<IncomingMessage> ReceiveOneAsync(
        NatsEventBus bus, CancellationToken cancellationToken)
    {
        await foreach (var message in bus.ConsumeWithAckAsync(cancellationToken))
        {
            await message.Ack();
            return message;
        }
        throw new InvalidOperationException("The NATS consumer completed without receiving a message.");
    }

    private static async Task<IReadOnlyList<IncomingMessage>> ReceiveEventsAsync(
        NatsEventBus bus, int count, CancellationToken cancellationToken)
    {
        var received = new List<IncomingMessage>();
        await foreach (var message in bus.ConsumeWithAckAsync(cancellationToken))
        {
            await message.Ack();
            received.Add(message);
            if (received.Count == count) return received;
        }
        throw new InvalidOperationException("The NATS event subscriptions completed early.");
    }

    private static Dictionary<string, (Type, Type)> CreateEventMap(string applicationId) => new()
    {
        [MessagingNamingHelper.GetQueueName(typeof(OwnedEvent), typeof(OwnedEventHandlerA), applicationId)] =
            (typeof(OwnedEvent), typeof(OwnedEventHandlerA)),
        [MessagingNamingHelper.GetQueueName(typeof(OwnedEvent), typeof(OwnedEventHandlerB), applicationId)] =
            (typeof(OwnedEvent), typeof(OwnedEventHandlerB))
    };
}

public sealed class KafkaEventBusIntegrationTests : IAsyncLifetime
{
    private readonly KafkaContainer _container = new KafkaBuilder()
        .WithImage("confluentinc/cp-kafka:7.7.1")
        .WithCleanUp(true)
        .Build();

    public Task InitializeAsync() => _container.StartAsync();
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    [Fact]
    public async Task Kafka_preserves_command_ownership_and_independent_event_groups()
    {
        var prefix = $"lycia-{Guid.NewGuid():N}";
        var ownerOptions = new KafkaEventBusOptions
        {
            BootstrapServers = _container.GetBootstrapAddress(),
            ApplicationId = "OwnerService",
            TopicPrefix = prefix
        };
        var senderOptions = new KafkaEventBusOptions
        {
            BootstrapServers = ownerOptions.BootstrapServers,
            ApplicationId = "RequesterService",
            TopicPrefix = prefix,
            EnsureTopics = false
        };
        var queue = MessagingNamingHelper.GetQueueName(
            typeof(OwnedCommand), typeof(OwnedCommandHandler), ownerOptions.ApplicationId);
        var map = new Dictionary<string, (Type, Type)>
        {
            [queue] = (typeof(OwnedCommand), typeof(OwnedCommandHandler))
        };
        foreach (var registration in CreateEventMap(ownerOptions.ApplicationId))
            map.Add(registration.Key, registration.Value);
        var serializer = new NewtonsoftJsonMessageSerializer();

        await using var consumer = new KafkaEventBus(map, ownerOptions, serializer);
        await using var publisher = new KafkaEventBus(
            new Dictionary<string, (Type, Type)>(), senderOptions, serializer);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var commandReceived = new TaskCompletionSource<IncomingMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var eventsReceived = new TaskCompletionSource<IReadOnlyList<IncomingMessage>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var pump = PumpAsync(consumer, commandReceived, eventsReceived, timeout.Token);
        await Task.Delay(3000, timeout.Token);
        var command = new OwnedCommand { Value = "kafka" };
        await publisher.Send(command, cancellationToken: timeout.Token);

        var incoming = await commandReceived.Task.WaitAsync(timeout.Token);
        incoming.MessageType.Should().Be(typeof(OwnedCommand));
        incoming.Headers["ReplyTo"].Should().Be("RequesterService");

        await publisher.Publish(new OwnedEvent { Value = "kafka-event" }, cancellationToken: timeout.Token);
        var eventHandlers = (await eventsReceived.Task.WaitAsync(timeout.Token))
            .Select(message => message.HandlerType).ToArray();
        eventHandlers.Should().Contain(typeof(OwnedEventHandlerA));
        eventHandlers.Should().Contain(typeof(OwnedEventHandlerB));
        timeout.Cancel();
        try { await pump; }
        catch (OperationCanceledException) { }
    }

    private static async Task PumpAsync(
        KafkaEventBus bus,
        TaskCompletionSource<IncomingMessage> commandReceived,
        TaskCompletionSource<IReadOnlyList<IncomingMessage>> eventsReceived,
        CancellationToken cancellationToken)
    {
        var events = new List<IncomingMessage>();
        await foreach (var message in bus.ConsumeWithAckAsync(cancellationToken))
        {
            await message.Ack();
            if (message.MessageType == typeof(OwnedCommand))
                commandReceived.TrySetResult(message);
            else if (message.MessageType == typeof(OwnedEvent))
            {
                events.Add(message);
                if (events.Count == 2) eventsReceived.TrySetResult(events);
            }
        }
    }

    private static Dictionary<string, (Type, Type)> CreateEventMap(string applicationId) => new()
    {
        [MessagingNamingHelper.GetQueueName(typeof(OwnedEvent), typeof(OwnedEventHandlerA), applicationId)] =
            (typeof(OwnedEvent), typeof(OwnedEventHandlerA)),
        [MessagingNamingHelper.GetQueueName(typeof(OwnedEvent), typeof(OwnedEventHandlerB), applicationId)] =
            (typeof(OwnedEvent), typeof(OwnedEventHandlerB))
    };
}

public interface IOwnerServiceCommand : ICommandEndpoint;

public sealed class OwnedCommand : CommandBase, IOwnerServiceCommand
{
    public string Value { get; set; } = string.Empty;
}

public sealed class OwnedCommandHandler;
public sealed class OwnedEvent : EventBase
{
    public string Value { get; set; } = string.Empty;
}
public sealed class OwnedEventHandlerA;
public sealed class OwnedEventHandlerB;
