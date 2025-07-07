// All async operations are now cancellation-aware and propagate the CancellationToken for graceful shutdown and responsiveness.

using Lycia.Messaging;
using Lycia.Saga.Abstractions;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using Lycia.Infrastructure.Helpers;
using Lycia.Saga.Helpers;

namespace Lycia.Extensions.Eventing;

public sealed class RabbitMqEventBus : IEventBus, IAsyncDisposable
{
    private readonly ConnectionFactory _factory;
    private readonly ILogger<RabbitMqEventBus> _logger;
    private IConnection? _connection;
    private IChannel? _channel;
    private readonly IDictionary<string, Type> _queueTypeMap;
    private readonly List<AsyncEventingBasicConsumer> _consumers = [];
    private readonly SemaphoreSlim _connectionLock = new(1, 1);

    private RabbitMqEventBus(string? conn, ILogger<RabbitMqEventBus> logger, IDictionary<string, Type> queueTypeMap)
    {
        _logger = logger;
        _queueTypeMap = queueTypeMap;

        _factory = new ConnectionFactory
        {
            Endpoint = new AmqpTcpEndpoint(conn),
            AutomaticRecoveryEnabled = true
        };
    }

    public static async Task<RabbitMqEventBus> CreateAsync(string? conn, ILogger<RabbitMqEventBus> logger,
        IDictionary<string, Type> queueTypeMap, CancellationToken cancellationToken = default)
    {
        var bus = new RabbitMqEventBus(conn, logger, queueTypeMap);
        await bus.ConnectAsync(cancellationToken).ConfigureAwait(false);
        return bus;
    }


    private async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        _connection = await _factory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureChannelAsync(CancellationToken cancellationToken = default)
    {
        if (_channel is { IsOpen: true })
            return;

        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            if (_channel is { IsOpen: true })
                return;

            if (_connection is null || !_connection.IsOpen)
            {
                _logger.LogWarning("RabbitMQ connection lost. Reconnecting...");
                await ConnectAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async Task Publish<TEvent>(TEvent @event, Guid? sagaId = null, CancellationToken cancellationToken = default)
        where TEvent : IEvent
    {
        await EnsureChannelAsync(cancellationToken).ConfigureAwait(false);
        var routingKey = RoutingKeyHelper.GetRoutingKey(typeof(TEvent));
        var exchangeName = routingKey;

        if (_channel == null)
        {
            throw new InvalidOperationException(
                "Channel is not initialized. Ensure RabbitMqEventBus is properly created.");
        }

        await _channel.ExchangeDeclareAsync(
            exchange: exchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            arguments: null, cancellationToken: cancellationToken);

        // Prepare headers:
        // "SagaId" - from event.SagaId if present, else sagaId parameter if provided
        // "CorrelationId", "MessageId", "ParentMessageId", "Timestamp", "ApplicationId" - extracted from event if present, not generated
        // "CausationId" - new Guid for this message if not present in event
        // "EventType" - full name of the event type
        // "PublishedAt" - UTC ISO-8601 timestamp of publishing
        var headers = new Dictionary<string, object?>();
        var eventBase = @event as dynamic;

        // SagaId preference: event.SagaId if available, else sagaId parameter
        Guid? effectiveSagaId = null;
        try
        {
            effectiveSagaId = eventBase?.SagaId;
        }
        catch
        {
        }

        if (effectiveSagaId == null)
            effectiveSagaId = sagaId;

        if (effectiveSagaId.HasValue)
            headers["SagaId"] = effectiveSagaId.Value.ToString();

        // CorrelationId
        try
        {
            var correlationId = eventBase?.CorrelationId;
            if (correlationId is Guid guidCorrelationId && guidCorrelationId != Guid.Empty)
                headers["CorrelationId"] = guidCorrelationId.ToString();
            else
                headers["CorrelationId"] = (effectiveSagaId ?? Guid.NewGuid()).ToString();
        }
        catch
        {
            headers["CorrelationId"] = (effectiveSagaId ?? Guid.NewGuid()).ToString();
        }

        // MessageId
        try
        {
            var messageId = eventBase?.MessageId;
            if (messageId is Guid guidMessageId && guidMessageId != Guid.Empty)
                headers["MessageId"] = guidMessageId.ToString();
        }
        catch
        {
        }

        // ParentMessageId
        try
        {
            var parentMessageId = eventBase?.ParentMessageId;
            if (parentMessageId is Guid guidParentMessageId && guidParentMessageId != Guid.Empty)
                headers["ParentMessageId"] = guidParentMessageId.ToString();
        }
        catch
        {
        }

        // Timestamp
        try
        {
            var timestamp = eventBase?.Timestamp;
            if (timestamp is DateTime dtTimestamp && dtTimestamp != default)
                headers["Timestamp"] = dtTimestamp.ToString("o");
        }
        catch
        {
        }

        // ApplicationId
        try
        {
            var applicationId = eventBase?.ApplicationId;
            if (applicationId is string appId && !string.IsNullOrWhiteSpace(appId))
                headers["ApplicationId"] = appId;
        }
        catch
        {
        }

        // CausationId
        try
        {
            var causationId = eventBase?.CausationId;
            if (causationId is Guid guidCausationId && guidCausationId != Guid.Empty)
                headers["CausationId"] = guidCausationId.ToString();
            else
                headers["CausationId"] = Guid.NewGuid().ToString();
        }
        catch
        {
            headers["CausationId"] = Guid.NewGuid().ToString();
        }

        headers["EventType"] = typeof(TEvent).FullName;
        headers["PublishedAt"] = DateTime.UtcNow.ToString("o");

        var properties = new BasicProperties
        {
            Persistent = true,
            Headers = headers
        };

        var json = JsonHelper.SerializeSafe(@event);
        var body = Encoding.UTF8.GetBytes(json);

        await _channel.BasicPublishAsync(
            exchange: exchangeName,
            routingKey: routingKey,
            mandatory: false,
            basicProperties: properties,
            body: body, cancellationToken: cancellationToken);
    }

    public async Task Send<TCommand>(TCommand command, Guid? sagaId = null,
        CancellationToken cancellationToken = default) where TCommand : ICommand
    {
        await EnsureChannelAsync(cancellationToken).ConfigureAwait(false);
        var queueName = RoutingKeyHelper.GetRoutingKey(typeof(TCommand));
        var routingKey = queueName;

        await _channel?.QueueDeclareAsync(
            queue: queueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null, cancellationToken: cancellationToken)!;

        // Prepare headers:
        // "SagaId" - from command.SagaId if present, else sagaId parameter if provided
        // "CorrelationId", "MessageId", "ParentMessageId", "Timestamp", "ApplicationId" - extracted from command if present, not generated
        // "CausationId" - new Guid for this message if not present in command
        // "CommandType" - full name of the command type
        // "PublishedAt" - UTC ISO-8601 timestamp of publishing
        var headers = new Dictionary<string, object?>();
        var commandBase = command as dynamic;

        // SagaId preference: command.SagaId if available, else sagaId parameter
        Guid? effectiveSagaId = null;
        try
        {
            effectiveSagaId = commandBase?.SagaId;
        }
        catch
        {
        }

        if (effectiveSagaId == null)
            effectiveSagaId = sagaId;

        if (effectiveSagaId.HasValue)
            headers["SagaId"] = effectiveSagaId.Value.ToString();

        // CorrelationId
        try
        {
            var correlationId = commandBase?.CorrelationId;
            if (correlationId is Guid guidCorrelationId && guidCorrelationId != Guid.Empty)
                headers["CorrelationId"] = guidCorrelationId.ToString();
            else
                headers["CorrelationId"] = (effectiveSagaId ?? Guid.NewGuid()).ToString();
        }
        catch
        {
            headers["CorrelationId"] = (effectiveSagaId ?? Guid.NewGuid()).ToString();
        }

        // MessageId
        try
        {
            var messageId = commandBase?.MessageId;
            if (messageId is Guid guidMessageId && guidMessageId != Guid.Empty)
                headers["MessageId"] = guidMessageId.ToString();
        }
        catch
        {
        }

        // ParentMessageId
        try
        {
            var parentMessageId = commandBase?.ParentMessageId;
            if (parentMessageId is Guid guidParentMessageId && guidParentMessageId != Guid.Empty)
                headers["ParentMessageId"] = guidParentMessageId.ToString();
        }
        catch
        {
        }

        // Timestamp
        try
        {
            var timestamp = commandBase?.Timestamp;
            if (timestamp is DateTime dtTimestamp && dtTimestamp != default)
                headers["Timestamp"] = dtTimestamp.ToString("o");
        }
        catch
        {
        }

        // ApplicationId
        try
        {
            var applicationId = commandBase?.ApplicationId;
            if (applicationId is string appId && !string.IsNullOrWhiteSpace(appId))
                headers["ApplicationId"] = appId;
        }
        catch
        {
        }

        // CausationId
        try
        {
            var causationId = commandBase?.CausationId;
            if (causationId is Guid guidCausationId && guidCausationId != Guid.Empty)
                headers["CausationId"] = guidCausationId.ToString();
            else
                headers["CausationId"] = Guid.NewGuid().ToString();
        }
        catch
        {
            headers["CausationId"] = Guid.NewGuid().ToString();
        }

        headers["CommandType"] = typeof(TCommand).FullName;
        headers["PublishedAt"] = DateTime.UtcNow.ToString("o");

        var properties = new BasicProperties
        {
            Persistent = true,
            Headers = headers
        };

        var json = JsonHelper.SerializeSafe(command);
        var body = Encoding.UTF8.GetBytes(json);

        await _channel.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: routingKey,
            mandatory: false,
            basicProperties: properties,
            body: body, cancellationToken: cancellationToken);
    }

    private async Task PublishToDeadLetterQueueAsync(string dlqName, byte[] body, IReadOnlyBasicProperties props,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureChannelAsync(cancellationToken);
            if (_channel == null)
                throw new InvalidOperationException("Channel is not initialized for DLQ publish.");

            await _channel.QueueDeclareAsync(
                queue: dlqName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null,
                cancellationToken: cancellationToken);

            // For RabbitMQ.Client 7.x+ this is the only valid way:
            var basicProps = props as BasicProperties ?? new BasicProperties();
            if (props != basicProps)
            {
                basicProps.Headers = props.Headers;
                basicProps.CorrelationId = props.CorrelationId;
                basicProps.ContentType = props.ContentType;
                basicProps.MessageId = props.MessageId;
                basicProps.Type = props.Type;
                basicProps.UserId = props.UserId;
                basicProps.AppId = props.AppId;
                basicProps.ClusterId = props.ClusterId;
                basicProps.ContentEncoding = props.ContentEncoding;
                basicProps.DeliveryMode = props.DeliveryMode;
                basicProps.Expiration = props.Expiration;
                basicProps.Priority = props.Priority;
                basicProps.ReplyTo = props.ReplyTo;
                basicProps.Timestamp = props.Timestamp;
                basicProps.Persistent = props.Persistent;
                basicProps.ReplyToAddress = props.ReplyToAddress;
            }

            await _channel.BasicPublishAsync(
                exchange: "",
                routingKey: dlqName,
                mandatory: false,
                basicProps,
                body,
                cancellationToken
            );

            _logger.LogWarning("Dead-lettered message published to DLQ: {DlqName}", dlqName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish message to dead letter queue: {DlqName}", dlqName);
        }
    }

    private async IAsyncEnumerable<(byte[] Body, Type MessageType)> ConsumeAsync(
        IDictionary<string, Type> queueTypeMap,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (_channel == null)
            throw new InvalidOperationException(
                "Channel is not initialized. Ensure RabbitMqEventBus is properly created.");

        var messageQueue = new ConcurrentQueue<(byte[] Body, Type MessageType)>();

        foreach (var (queueName, messageType) in queueTypeMap)
        {
            var consumer = new AsyncEventingBasicConsumer(_channel);

            // This pattern ensures that message handling errors are caught and do not crash the consumer loop.
            // Instead, problematic messages are logged and can be dead-lettered for later analysis.
            consumer.ReceivedAsync += async (model, ea) =>
            {
                try
                {
                    messageQueue.Enqueue((ea.Body.ToArray(), messageType));
                    await Task.CompletedTask;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Failed to process message from queue '{QueueName}' of type '{MessageType}'. Dead-lettering the message",
                        queueName, messageType.FullName);
                    // DLQ logic:
                    await PublishToDeadLetterQueueAsync(queueName + ".dlq", ea.Body.ToArray(), ea.BasicProperties,
                        cancellationToken);
                }
            };

            await _channel.BasicConsumeAsync(
                queue: queueName,
                autoAck: true,
                consumer: consumer, cancellationToken: cancellationToken);

            _consumers.Add(consumer);
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            while (messageQueue.TryDequeue(out var result))
                yield return result;

            await Task.Delay(50, cancellationToken);
        }
    }

    public IAsyncEnumerable<(byte[] Body, Type MessageType)> ConsumeAsync(CancellationToken cancellationToken)
    {
        if (_queueTypeMap == null)
            throw new InvalidOperationException(
                "Queue/message type map is not configured for this event bus instance.");
        return ConsumeAsync(_queueTypeMap, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            // Explicitly cancel and clean up all consumers
            // This ensures there are no orphaned consumers on the channel during shutdown.
            if (_channel != null)
            {
                foreach (var consumer in _consumers)
                {
                    try
                    {
                        if (consumer.ConsumerTags.Length == 0) continue;
                        foreach (var tag in consumer.ConsumerTags)
                            await _channel.BasicCancelAsync(consumerTag: tag)
                                .ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to cancel consumer with tag {ConsumerTags}",
                            string.Join(", ", consumer.ConsumerTags));
                    }
                }

                _consumers.Clear();
            }

            if (_channel != null)
            {
                try
                {
                    await _channel.CloseAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "RabbitMQ channel CloseAsync failed");
                }

                try
                {
                    await _channel.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "RabbitMQ channel DisposeAsync failed");
                }

                _channel = null;
            }

            if (_connection != null)
            {
                try
                {
                    await _connection.CloseAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "RabbitMQ connection CloseAsync failed");
                }

                try
                {
                    await _connection.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "RabbitMQ connection DisposeAsync failed");
                }

                _connection = null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RabbitMQ cleanup failed");
        }
    }
}