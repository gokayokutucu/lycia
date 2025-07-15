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
#if NET6_0_OR_GREATER
    private IChannel? _channel; 
#else
    private IModel? _channel;
#endif
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
#if NET6_0_OR_GREATER
    _connection = await _factory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
    _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
#else
        _connection = _factory.CreateConnection();
        _channel = _connection.CreateModel();
#endif
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
#if NET6_0_OR_GREATER
            _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
#else
                _channel = _connection.CreateModel();
#endif
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

        // Declare DLX and DLQ for this exchange (producer-side responsibility)
        await DeclareDeadLetter(exchangeName, cancellationToken);

#if NET6_0_OR_GREATER
        // Declare the exchange (topic) and publish to it. No queue or binding logic here.
        await _channel.ExchangeDeclareAsync(
            exchange: exchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            arguments: null, cancellationToken: cancellationToken);
#else
        _channel.ExchangeDeclare(
            exchange: exchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            arguments: null);
#endif

        sagaId ??= @event.SagaId; // Ensure sagaId is not null, use a new Guid if not provided

        var headers = RabbitMqEventBusHelper.BuildMessageHeaders(@event, sagaId, typeof(TEvent), Constants.EventTypeHeader);
#if NET6_0_OR_GREATER
        var properties = new BasicProperties
        {
            Persistent = true,
            Headers = headers
        }; 
#else
        var properties = _channel.CreateBasicProperties();
        properties.Persistent = true;
        properties.Headers = headers;

#endif

        var json = JsonHelper.SerializeSafe(@event);
        var body = Encoding.UTF8.GetBytes(json);

#if NET6_0_OR_GREATER
        await _channel.BasicPublishAsync(
            exchange: exchangeName,
            routingKey: exchangeName,
            mandatory: false,
            basicProperties: properties,
            body: body, cancellationToken: cancellationToken); 
#else
        _channel.BasicPublish(
            exchange: exchangeName,
            routingKey: exchangeName,
            mandatory: false,
            basicProperties: properties,
            body: body);
#endif
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

        // Producer-side: declare DLX and DLQ for this queue
        var queueArgs = await DeclareDeadLetter(queueName, cancellationToken);
        // Add TTL if configured
        if (_options.MessageTTL is { TotalMilliseconds: > 0 } ttl)
        {
            queueArgs[XMesssageTtl] = (int)ttl.TotalMilliseconds;
        }
        // Ensure queue is declared with DLQ/DLX and TTL args (idempotent)
        await _channel.QueueDeclareAsync(
            queue: queueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: queueArgs.Count > 0 ? queueArgs : null,
            cancellationToken: cancellationToken);

        var headers = RabbitMqEventBusHelper.BuildMessageHeaders(command, sagaId, typeof(TCommand), Constants.CommandTypeHeader);
#if NET6_0_OR_GREATER
        var properties = new BasicProperties
        {
            Persistent = true,
            Headers = headers
        }; 
#else
        var properties = _channel.CreateBasicProperties();
        properties.Persistent = true;
        properties.Headers = headers;

#endif

        var json = JsonHelper.SerializeSafe(command);
        var body = Encoding.UTF8.GetBytes(json);

#if NET6_0_OR_GREATER
        await _channel.BasicPublishAsync(
           exchange: string.Empty,
           routingKey: queueName,
           mandatory: false,
           basicProperties: properties,
           body: body, cancellationToken: cancellationToken); 
#else
        _channel.BasicPublish(
           exchange: string.Empty,
           routingKey: queueName,
           mandatory: false,
           basicProperties: properties,
           body: body);
#endif
    }

    private async Task PublishToDeadLetterQueueAsync(string dlqName, byte[] body,
#if NET6_0_OR_GREATER
        IReadOnlyBasicProperties props, 
#else
        IBasicProperties props,
#endif
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
#if NET6_0_OR_GREATER
            await _channel.QueueDeclareAsync(
                    queue: dlqName,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: dlqArgs.Count > 0 ? dlqArgs : null,
                    cancellationToken: cancellationToken); 
#else
            _channel.QueueDeclare(
                    queue: dlqName,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: dlqArgs.Count > 0 ? dlqArgs : null);
#endif

            // For RabbitMQ.Client 7.x+ this is the only valid way:
#if NET6_0_OR_GREATER
            var basicProps = props as BasicProperties ?? new BasicProperties(); 
#else
            var basicProps = props as IBasicProperties ?? _channel.CreateBasicProperties();
#endif
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

#if NET6_0_OR_GREATER
            await _channel.BasicPublishAsync(
                    exchange: string.Empty,
                    routingKey: dlqName,
                    mandatory: false,
                    basicProps,
                    body,
                    cancellationToken
                ); 
#else
            _channel.BasicPublish(
                    exchange: string.Empty,
                    routingKey: dlqName,
                    mandatory: false,
                    basicProps,
                    body
                );
#endif

            _logger.LogWarning("Dead-lettered message published to DLQ: {DlqName}", dlqName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish message to dead letter queue: {DlqName}", dlqName);
        }
    }

    private async IAsyncEnumerable<(byte[] Body, Type MessageType)> ConsumeAsync(
        IDictionary<string, Type> queueTypeMap, bool autoAck = true,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_channel == null)
            throw new InvalidOperationException(
                "Channel is not initialized. Ensure RabbitMqEventBus is properly created.");

        var messageQueue = new ConcurrentQueue<(byte[] Body, Type MessageType)>();

        // queueName => e.g. Full format: event.OrderCreatedEvent.CreateOrderSagaHandler.OrderService
        foreach (var kvp in queueTypeMap)
        {
            var queueName = kvp.Key;
            var messageType = kvp.Value;
            // Ensure queue and exchange exist and are bound before subscribing the consumer.
            // These operations are idempotent.
            var exchangeName = MessagingNamingHelper.GetExchangeName(messageType); // e.g., "event.OrderCreatedEvent"
            var routingKey = MessagingNamingHelper.GetTopicRoutingKey(messageType); // e.g., "event.OrderCreatedEvent.#"

            // Declare exchange (topic)
#if NET6_0_OR_GREATER
            await _channel.ExchangeDeclareAsync(
                exchange: exchangeName,
                type: ExchangeType.Topic,
                durable: true,
                autoDelete: false,
                arguments: null,
                cancellationToken: cancellationToken); 
#else
            _channel.ExchangeDeclare(
                exchange: exchangeName,
                type: ExchangeType.Topic,
                durable: true,
                autoDelete: false,
                arguments: null);
#endif

            // Declare queue
            var queueArgs = new Dictionary<string, object?>();
            if (_options?.MessageTTL is { TotalMilliseconds: > 0 } ttl)
            {
                queueArgs[XMesssageTtl] = (int)ttl.TotalMilliseconds;
            }

#if NET6_0_OR_GREATER
            await _channel.QueueDeclareAsync(
                    queue: queueName,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: queueArgs.Count > 0 ? queueArgs : null,
                    cancellationToken: cancellationToken); 
#else
            _channel.QueueDeclare(
                    queue: queueName,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: queueArgs.Count > 0 ? queueArgs : null);
#endif

            // Bind queue to exchange with queue name as a routing key
#if NET6_0_OR_GREATER
            await _channel.QueueBindAsync(
                    queue: queueName,
                    exchange: exchangeName,
                    routingKey: routingKey,
                    arguments: null,
                    cancellationToken: cancellationToken); 
#else
            _channel.QueueBind(
                    queue: queueName,
                    exchange: exchangeName,
                    routingKey: routingKey,
                    arguments: null);
#endif

            var consumer = new AsyncEventingBasicConsumer(_channel);

            // This pattern ensures that message handling errors are caught and do not crash the consumer loop.
            // Instead, problematic messages are logged and can be dead-lettered for later analysis.
#if NET6_0_OR_GREATER
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
# else
            consumer.Received += (_, ea) =>
            {
                try
                {
                    messageQueue.Enqueue((ea.Body.ToArray(), messageType));
                    return Task.CompletedTask;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Failed to process message from queue '{QueueName}' of type '{MessageType}'. Dead-lettering the message",
                        queueName, messageType.FullName);
                    // DLQ logic:
                    PublishToDeadLetterQueueAsync(queueName + ".dlq", ea.Body.ToArray(), ea.BasicProperties,
                        cancellationToken).GetAwaiter().GetResult();
                    return Task.CompletedTask;
                }
            };
#endif
#if NET6_0_OR_GREATER
                    queue: queueName,
                    autoAck: true,
                    consumer: consumer, cancellationToken: cancellationToken); 
#else
            _channel.BasicConsume(
                    queue: queueName,
                    autoAck: autoAck,
                    consumer: consumer);
#endif
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

    private async Task<Dictionary<string, object?>> DeclareDeadLetter(string queueName, CancellationToken cancellationToken)
    {
        var dlxExchange = $"{queueName}.dlx";
        var dlqName = $"{queueName}.dlq";
        
        
        // DLX (Dead Letter Exchange) declare
        await _channel!.ExchangeDeclareAsync(
            exchange: dlxExchange,
            type: ExchangeType.Direct,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        // DLQ (Dead Letter Queue) declare
        await _channel!.QueueDeclareAsync(
            queue: dlqName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        // DLQ binding
        await _channel!.QueueBindAsync(
            queue: dlqName,
            exchange: dlxExchange,
            routingKey: dlqName,
            arguments: null,
            cancellationToken: cancellationToken);

        return new Dictionary<string, object?>
        {
            ["x-dead-letter-exchange"] = dlxExchange,
            ["x-dead-letter-routing-key"] = dlqName
        };
    }

    public IAsyncEnumerable<(byte[] Body, Type MessageType)> ConsumeAsync(bool autoAck = true, CancellationToken cancellationToken = default)
    {
        if (_queueTypeMap == null)
            throw new InvalidOperationException(
                "Queue/message type map is not configured for this event bus instance.");
        return ConsumeAsync(_queueTypeMap, autoAck, cancellationToken);
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
#if NET6_0_OR_GREATER
                        await _channel.BasicCancelAsync(consumerTag: tag).ConfigureAwait(false); 
#else
                        _channel.BasicCancel(consumerTag: tag);
#endif
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
#if NET6_0_OR_GREATER
            await _connection!.CloseAsync().ConfigureAwait(false); 
#else
            _connection!.Close();
#endif
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RabbitMQ connection CloseAsync failed");
        }

        try
        {
#if NET6_0_OR_GREATER
            await _connection!.DisposeAsync().ConfigureAwait(false); 
#else
            _connection!.Dispose();
#endif
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
#if NET6_0_OR_GREATER
            await _channel!.CloseAsync().ConfigureAwait(false); 
#else
            _channel!.Close();
#endif
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RabbitMQ channel CloseAsync failed");
        }

        try
        {
#if NET6_0_OR_GREATER
            await _channel!.DisposeAsync().ConfigureAwait(false); 
#else
            _channel!.Dispose();
#endif
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RabbitMQ channel DisposeAsync failed");
        }
    }
}