using Lycia.Messaging;
using Lycia.Saga.Abstractions;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
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

    public static async Task<RabbitMqEventBus> CreateAsync(string? conn, ILogger<RabbitMqEventBus> logger, IDictionary<string, Type> queueTypeMap)
    {
        var bus = new RabbitMqEventBus(conn, logger, queueTypeMap);
        await bus.ConnectAsync().ConfigureAwait(false);
        return bus;
    }


    private async Task ConnectAsync()
    {
        _connection = await _factory.CreateConnectionAsync().ConfigureAwait(false);
        _channel = await _connection.CreateChannelAsync().ConfigureAwait(false);
    }

    private async Task EnsureChannelAsync()
    {
        if (_channel is { IsOpen: true })
            return;

        await _connectionLock.WaitAsync();
        try
        {
            if (_channel is { IsOpen: true })
                return;

            if (_connection is null || !_connection.IsOpen)
            {
                _logger.LogWarning("RabbitMQ connection lost. Reconnecting...");
                await ConnectAsync().ConfigureAwait(false);
            }
            else
            {
                _channel = await _connection.CreateChannelAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
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

    public async Task Publish<TEvent>(TEvent @event, Guid? sagaId = null) where TEvent : IEvent
    {
        await EnsureChannelAsync().ConfigureAwait(false);
        var routingKey = RoutingKeyHelper.GetRoutingKey(typeof(TEvent));
        var exchangeName = routingKey;

        if (_channel == null)
        {
            throw new InvalidOperationException("Channel is not initialized. Ensure RabbitMqEventBus is properly created.");
        }

        await _channel.ExchangeDeclareAsync(
            exchange: exchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            arguments: null);

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
        catch { }
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
        catch { }

        // ParentMessageId
        try
        {
            var parentMessageId = eventBase?.ParentMessageId;
            if (parentMessageId is Guid guidParentMessageId && guidParentMessageId != Guid.Empty)
                headers["ParentMessageId"] = guidParentMessageId.ToString();
        }
        catch { }

        // Timestamp
        try
        {
            var timestamp = eventBase?.Timestamp;
            if (timestamp is DateTime dtTimestamp && dtTimestamp != default)
                headers["Timestamp"] = dtTimestamp.ToString("o");
        }
        catch { }

        // ApplicationId
        try
        {
            var applicationId = eventBase?.ApplicationId;
            if (applicationId is string appId && !string.IsNullOrWhiteSpace(appId))
                headers["ApplicationId"] = appId;
        }
        catch { }

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
            body: body);
    }

    public async Task Send<TCommand>(TCommand command, Guid? sagaId = null) where TCommand : ICommand
    {
        await EnsureChannelAsync().ConfigureAwait(false);
        var queueName = RoutingKeyHelper.GetRoutingKey(typeof(TCommand));
        var routingKey = queueName;

        await _channel?.QueueDeclareAsync(
            queue: queueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null)!;

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
        catch { }
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
        catch { }

        // ParentMessageId
        try
        {
            var parentMessageId = commandBase?.ParentMessageId;
            if (parentMessageId is Guid guidParentMessageId && guidParentMessageId != Guid.Empty)
                headers["ParentMessageId"] = guidParentMessageId.ToString();
        }
        catch { }

        // Timestamp
        try
        {
            var timestamp = commandBase?.Timestamp;
            if (timestamp is DateTime dtTimestamp && dtTimestamp != default)
                headers["Timestamp"] = dtTimestamp.ToString("o");
        }
        catch { }

        // ApplicationId
        try
        {
            var applicationId = commandBase?.ApplicationId;
            if (applicationId is string appId && !string.IsNullOrWhiteSpace(appId))
                headers["ApplicationId"] = appId;
        }
        catch { }

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
            body: body);
    }

    private async IAsyncEnumerable<(byte[] Body, Type MessageType)> ConsumeAsync(
        IDictionary<string, Type> queueTypeMap,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (_channel == null)
            throw new InvalidOperationException("Channel is not initialized. Ensure RabbitMqEventBus is properly created.");

        var messageQueue = new ConcurrentQueue<(byte[] Body, Type MessageType)>();

        foreach (var (queueName, messageType) in queueTypeMap)
        {
            var consumer = new AsyncEventingBasicConsumer(_channel);

            consumer.ReceivedAsync += async (model, ea) =>
            {
                messageQueue.Enqueue((ea.Body.ToArray(), messageType));
                await Task.CompletedTask;
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
            throw new InvalidOperationException("Queue/message type map is not configured for this event bus instance.");
        return ConsumeAsync(_queueTypeMap, cancellationToken);
    }
}
