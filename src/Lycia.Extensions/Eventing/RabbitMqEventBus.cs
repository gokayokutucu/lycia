// All async operations are now cancellation-aware and propagate the CancellationToken for graceful shutdown and responsiveness.

using Lycia.Messaging;
using Lycia.Saga.Abstractions;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Lycia.Infrastructure.Helpers;
using Lycia.Saga.Helpers;

using Lycia.Extensions.Configurations;
using Lycia.Extensions.Helpers;
using Constants = Lycia.Extensions.Configurations.Constants;

namespace Lycia.Extensions.Eventing;

public sealed class RabbitMqEventBus : IEventBus, IAsyncDisposable
{
    private const string XMesssageTtl = "x-message-ttl";
    
    private readonly ConnectionFactory _factory;
    private readonly ILogger<RabbitMqEventBus> _logger;
    private IConnection? _connection;
    private IChannel? _channel;
    private readonly IDictionary<string, Type> _queueTypeMap;
    private readonly List<AsyncEventingBasicConsumer> _consumers = [];
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly EventBusOptions _options;

    private RabbitMqEventBus(string? conn, ILogger<RabbitMqEventBus> logger, IDictionary<string, Type> queueTypeMap, EventBusOptions options)
    {
        _logger = logger;
        _queueTypeMap = queueTypeMap;
        _options = options;

        if (conn == null) throw new InvalidOperationException("RabbitMqEventBus connection is null");
        
        _factory = new ConnectionFactory
        {
            Uri = new Uri(conn),
            AutomaticRecoveryEnabled = true
        };
    }

    public static async Task<RabbitMqEventBus> CreateAsync(
        string? conn,
        ILogger<RabbitMqEventBus> logger,
        IDictionary<string, Type> queueTypeMap,
        EventBusOptions options,
        CancellationToken cancellationToken = default)
    {
        var bus = new RabbitMqEventBus(conn, logger, queueTypeMap, options);
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

    public async Task Publish<TEvent>(TEvent @event, Type? handlerType = null, Guid? sagaId = null, CancellationToken cancellationToken = default)
        where TEvent : IEvent
    {
        await EnsureChannelAsync(cancellationToken).ConfigureAwait(false);
        // routingKey equivalent to the exchange name in RabbitMQ terminology
        var exchangeName = MessagingNamingHelper.GetExchangeName(typeof(TEvent)); // event.OrderCreatedEvent

        if (_channel == null)
        {
            throw new InvalidOperationException(
                "Channel is not initialized. Ensure RabbitMqEventBus is properly created.");
        }

        // Declare the exchange (topic) and publish to it. No queue or binding logic here.
        await _channel.ExchangeDeclareAsync(
            exchange: exchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            arguments: null, cancellationToken: cancellationToken);

        sagaId ??= @event.SagaId; // Ensure sagaId is not null, use a new Guid if not provided

        var headers = RabbitMqEventBusHelper.BuildMessageHeaders(@event, sagaId, typeof(TEvent), Constants.EventTypeHeader);
        var properties = new BasicProperties
        {
            Persistent = true,
            Headers = headers
        };

        var json = JsonHelper.SerializeSafe(@event);
        var body = Encoding.UTF8.GetBytes(json);

        await _channel.BasicPublishAsync(
            exchange: exchangeName,
            routingKey: exchangeName,
            mandatory: false,
            basicProperties: properties,
            body: body, cancellationToken: cancellationToken);
    }

    public async Task Send<TCommand>(TCommand command, Type? handlerType = null, Guid? sagaId = null,
        CancellationToken cancellationToken = default) where TCommand : ICommand
    {
        await EnsureChannelAsync(cancellationToken).ConfigureAwait(false);

        var queueName = handlerType is not null ? MessagingNamingHelper.GetRoutingKey(typeof(TCommand), handlerType, _options.ApplicationId) : // command.CreateOrderCommand.CreateOrderSagaHandler.OrderService
            GetCommandQueueName(typeof(TCommand));
        
        if (_channel == null)
        {
            throw new InvalidOperationException(
                "Channel is not initialized. Ensure RabbitMqEventBus is properly created.");
        }

        // Only publish to the default exchange with the routing key (queue name).
        // No queue declaration or binding is performed here; assumes consumer setup elsewhere.

        var headers = RabbitMqEventBusHelper.BuildMessageHeaders(command, sagaId, typeof(TCommand), Constants.CommandTypeHeader);
        var properties = new BasicProperties
        {
            Persistent = true,
            Headers = headers
        };

        var json = JsonHelper.SerializeSafe(command);
        var body = Encoding.UTF8.GetBytes(json);

        await _channel.BasicPublishAsync(
            exchange: string.Empty, // Default exchange
            routingKey: queueName,
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

            var dlqArgs = new Dictionary<string, object?>();
            if (_options.MessageTTL is { TotalMilliseconds: > 0 } ttl)
            {
                dlqArgs[XMesssageTtl] = (int)ttl.TotalMilliseconds;
            }
            await _channel.QueueDeclareAsync(
                queue: dlqName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: dlqArgs.Count > 0 ? dlqArgs : null,
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
                exchange: string.Empty,
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

        // queueName => e.g. Full format: event.OrderCreatedEvent.CreateOrderSagaHandler.OrderService
        foreach (var (queueName, messageType) in queueTypeMap)
        {
            // Ensure queue and exchange exist and are bound before subscribing the consumer.
            // These operations are idempotent.
            var exchangeName = MessagingNamingHelper.GetExchangeName(messageType); // e.g., "event.OrderCreatedEvent"
            var routingKey = MessagingNamingHelper.GetTopicRoutingKey(messageType); // e.g., "event.OrderCreatedEvent.#"
            
            await _channel.ExchangeDeclareAsync(
                exchange: exchangeName,
                type: ExchangeType.Topic,
                durable: true,
                autoDelete: false,
                arguments: null, cancellationToken: cancellationToken);
            
            // Declare queue
            var queueArgs = new Dictionary<string, object?>();
            if (_options?.MessageTTL is { TotalMilliseconds: > 0 } ttl)
            {
                queueArgs[XMesssageTtl] = (int)ttl.TotalMilliseconds;
            }
            
            await _channel.QueueDeclareAsync(
                queue: queueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: queueArgs.Count > 0 ? queueArgs : null,
                cancellationToken: cancellationToken);

            // Bind queue to exchange with queue name as routing key
            await _channel.QueueBindAsync(
                queue: queueName,
                exchange: exchangeName,
                routingKey: routingKey,
                arguments: null,
                cancellationToken: cancellationToken);

            var consumer = new AsyncEventingBasicConsumer(_channel);

            // This pattern ensures that message handling errors are caught and do not crash the consumer loop.
            // Instead, problematic messages are logged and can be dead-lettered for later analysis.
            consumer.ReceivedAsync += async (_, ea) =>
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
    
    /// <summary>
    /// Returns the queue name for the specified command message type.
    /// This assumes only one queue (one consumer) exists per command type.
    /// For event message types, multiple queues may exist, so this should not be used for events.
    /// </summary>
    private string GetCommandQueueName(Type messageType)
    {
        return _queueTypeMap.FirstOrDefault(kvp => kvp.Value == messageType).Key
               ?? throw new InvalidOperationException($"No handler type found for message type {messageType.FullName}");
    }

    /// <summary>
    /// Performs application-defined tasks associated with freeing, releasing, or
    /// resetting unmanaged resources asynchronously.</summary>
    /// <returns>A task that represents the asynchronous dispose operation.</returns>
    public async ValueTask DisposeAsync()
    {
        try
        {
            // Explicitly cancel and clean up all consumers
            // This ensures there are no orphaned consumers on the channel during shutdown.
            if (_channel != null)
            {
                var allTags = _consumers.SelectMany(consumer => consumer.ConsumerTags);
                foreach (var tag in allTags)
                {
                    try
                    {
                        await _channel.BasicCancelAsync(consumerTag: tag).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to cancel consumer with tag {ConsumerTag}", tag);
                    }
                }

                _consumers.Clear();
            }

            if (_channel != null)
            {
                await CloseAndDisposeChannelAsync();

                _channel = null;
            }

            if (_connection != null)
            {
                await CloseAndDisposeConnectionAsync();

                _connection = null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RabbitMQ cleanup failed");
        }
    }

    private async ValueTask CloseAndDisposeConnectionAsync()
    {
        try
        {
            await _connection!.CloseAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RabbitMQ connection CloseAsync failed");
        }

        try
        {
            await _connection!.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RabbitMQ connection DisposeAsync failed");
        }
    }

    private async ValueTask CloseAndDisposeChannelAsync()
    {
        try
        {
            await _channel!.CloseAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RabbitMQ channel CloseAsync failed");
        }

        try
        {
            await _channel!.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RabbitMQ channel DisposeAsync failed");
        }
    }
}