using Lycia.Messaging;
using Lycia.Saga.Abstractions;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using Lycia.Saga.Helpers;
using Lycia.Extensions.Configurations;
using Lycia.Extensions.Helpers;
using Constants = Lycia.Extensions.Configurations.Constants;


namespace Lycia.Extensions.Eventing;

#if NETSTANDARD2_0
public sealed class RabbitMqEventBus : IEventBus, IAsyncDisposable
{
    private const string XMesssageTtl = "x-message-ttl";
    private readonly ConnectionFactory _factory;
    private readonly ILogger<RabbitMqEventBus> _logger;
    private IConnection? _connection;
    private IModel? _channel;
    private readonly IDictionary<string, (Type MessageType, Type HandlerType)> _queueTypeMap;
    private readonly List<EventingBasicConsumer> _consumers = [];
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly EventBusOptions _options;

    private RabbitMqEventBus(string? conn, ILogger<RabbitMqEventBus> logger, IDictionary<string, (Type MessageType, Type HandlerType)> queueTypeMap, EventBusOptions options)
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

    public static async Task<RabbitMqEventBus> CreateAsync(string? conn,
					 
        ILogger<RabbitMqEventBus> logger,
        IDictionary<string, (Type MessageType, Type HandlerType)> queueTypeMap,
        EventBusOptions options,
        CancellationToken cancellationToken = default)
    {
        var bus = new RabbitMqEventBus(conn
            , logger
            , queueTypeMap
            , options);
        await bus
            .ConnectAsync(cancellationToken)
            .ConfigureAwait(false);
        return bus;
    }


    private async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        _connection = _factory
            .CreateConnection();

        _channel = _connection
            .CreateModel();

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
                await ConnectAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                _channel = _connection
                    .CreateModel();

            }
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async Task Publish<TEvent>(TEvent @event,
					   
        Type? handlerType = null, //Discard handlerType as it's not used in RabbitMQ
        Guid? sagaId = null,
        CancellationToken cancellationToken = default)
        where TEvent : IEvent
    {
        await EnsureChannelAsync(cancellationToken)
            .ConfigureAwait(false);
        // routingKey equivalent to the exchange name in RabbitMQ terminology
        var exchangeName = MessagingNamingHelper.GetExchangeName(typeof(TEvent)); // event.OrderCreatedEvent

        if (_channel == null)
        {
            throw new InvalidOperationException(
                "Channel is not initialized. Ensure RabbitMqEventBus is properly created.");
        }
        // Declare the exchange (topic) and publish to it. No queue or binding logic here.
        _channel.ExchangeDeclare(exchange: exchangeName
            , type: ExchangeType.Topic
            , durable: true
            , autoDelete: false
            , arguments: null);


        sagaId ??= @event.SagaId; // Ensure sagaId is not null, use a new Guid if not provided

        var headers = RabbitMqEventBusHelper.BuildMessageHeaders(@event
            , sagaId
            , typeof(TEvent)
            , Constants.EventTypeHeader);
        var properties = _channel.CreateBasicProperties();

        properties.Persistent = true;
        properties.Headers = headers;


        var json = JsonHelper.SerializeSafe(@event);
        var body = Encoding.UTF8.GetBytes(json);

        _channel.BasicPublish(exchange: exchangeName
            , routingKey: exchangeName
            , mandatory: false
            , basicProperties: properties
            , body: body);

    }

    public async Task Send<TCommand>(TCommand command,
						  
        Type? handlerType = null,
        Guid? sagaId = null,
        CancellationToken cancellationToken = default)
        where TCommand : ICommand
    {
        await EnsureChannelAsync(cancellationToken)
            .ConfigureAwait(false);

        var exchangeName = MessagingNamingHelper.GetExchangeName(typeof(TCommand)); // command.CreateOrderCommand
        var routingKey = MessagingNamingHelper.GetTopicRoutingKey(typeof(TCommand)); // e.g., "command.CreateOrderCommand.#"

        if (_channel == null)
        {
            throw new InvalidOperationException(
                "Channel is not initialized. Ensure RabbitMqEventBus is properly created.");
        }

        _channel.ExchangeDeclare(
            exchange: exchangeName,
            type: ExchangeType.Direct,
            durable: true,
            autoDelete: false,
            arguments: null);


        var headers = RabbitMqEventBusHelper.BuildMessageHeaders(command, sagaId, typeof(TCommand), Constants.CommandTypeHeader);
        var properties = _channel.CreateBasicProperties();

        properties.Persistent = true;
        properties.Headers = headers;


        var json = JsonHelper.SerializeSafe(command);
        var body = Encoding.UTF8.GetBytes(json);

        _channel.BasicPublish(
            exchange: exchangeName,
            routingKey: routingKey,
            mandatory: false,
            basicProperties: properties,
            body: body);

    }

    private async Task PublishToDeadLetterQueueAsync(string dlqName
        , byte[] body
        , IBasicProperties props
        , CancellationToken cancellationToken = default)
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
            _channel.QueueDeclare(
                queue: dlqName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: dlqArgs.Count > 0 ? dlqArgs : null);


            // For RabbitMQ.Client 7.x+ this is the only valid way:
            var basicProps = props as IBasicProperties ?? _channel.CreateBasicProperties();
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

            _channel.BasicPublish(
                exchange: string.Empty,
                routingKey: dlqName,
                mandatory: false,
                basicProps,
                body

                );

            _logger.LogWarning("Dead-lettered message published to DLQ: {DlqName}", dlqName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish message to dead letter queue: {DlqName}", dlqName);
        }
    }

    private async IAsyncEnumerable<(byte[] Body, Type MessageType, Type HandlerType)> ConsumeAsync(
		IDictionary<string, (Type MessageType, Type HandlerType)> queueTypeMap, bool autoAck = true,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_channel == null)
            throw new InvalidOperationException(
                "Channel is not initialized. Ensure RabbitMqEventBus is properly created.");

        var messageQueue = new ConcurrentQueue<(byte[] Body, Type MessageType, Type HandlerType)>();

        // queueName => e.g. Full format: event.OrderCreatedEvent.CreateOrderSagaHandler.OrderService
        foreach (var kvp in queueTypeMap)
        {
            var queueName = kvp.Key;
            var messageType = kvp.Value.MessageType;
            var handlerType = kvp.Value.HandlerType;

            // Ensure queue and exchange exist and are bound before subscribing the consumer.
            // These operations are idempotent.
            var exchangeName = MessagingNamingHelper.GetExchangeName(messageType); // e.g., "event.OrderCreatedEvent"
            var routingKey = MessagingNamingHelper.GetTopicRoutingKey(messageType); // e.g., "event.OrderCreatedEvent.#"

            var exchangeType = messageType.IsSubclassOf(typeof(EventBase))
                ? ExchangeType.Topic
                : ExchangeType.Direct;

            _channel.ExchangeDeclare(
                exchange: exchangeName,
                type: exchangeType,
                durable: true,
                autoDelete: false,
                arguments: null);


            // Declare the queue with DLX and DLQ arguments
            var queueArgs = await DeclareDeadLetter(queueName, cancellationToken);
            if (_options?.MessageTTL is { TotalMilliseconds: > 0 } ttl)
            {
                queueArgs[XMesssageTtl] = (int)ttl.TotalMilliseconds;
            }
            _channel.QueueDeclare(
                queue: queueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: queueArgs.Count > 0 ? queueArgs : null);


            // Bind queue to exchange with queue name as a routing key
            _channel.QueueBind(
                queue: queueName,
                exchange: exchangeName,
                routingKey: routingKey,
                arguments: null);

            var consumer = new EventingBasicConsumer(_channel);

            // This pattern ensures that message handling errors are caught and do not crash the consumer loop.
            // Instead, problematic messages are logged and can be dead-lettered for later analysis.
            consumer.Received += (_, ea) =>
            {
                try
                {
                    messageQueue.Enqueue((ea.Body.ToArray(), messageType, handlerType));
                    Task.Yield();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex
                        , "Failed to process message from queue '{QueueName}' of type '{MessageType}'. Dead-lettering the message"
                        , queueName
                        , messageType.FullName);

                    // DLQ logic:
                    PublishToDeadLetterQueueAsync(
                        queueName + ".dlq",
                        ea.Body.ToArray(),
                        ea.BasicProperties,
                        cancellationToken)
                    .GetAwaiter().GetResult();
                    Task.Yield();
                }
            };
            _channel.BasicConsume(
                queue: queueName,
                autoAck: true,
                consumer: consumer);


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
        try
        {
            var dlxExchange = $"{queueName}.dlx";
            var dlqName = $"{queueName}.dlq";


            // DLX (Dead Letter Exchange) declare
            _channel!.ExchangeDeclare(exchange: dlxExchange
                , type: ExchangeType.Direct
                , durable: true
                , autoDelete: false
                , arguments: null);


            // DLQ (Dead Letter Queue) declare
            _channel!.QueueDeclare(queue: dlqName
                , durable: true
                , exclusive: false
                , autoDelete: false
                , arguments: null);


            // DLQ binding
            _channel!.QueueBind(queue: dlqName
                , exchange: dlxExchange
                , routingKey: dlqName
                , arguments: null);

            return new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = dlxExchange,
                ["x-dead-letter-routing-key"] = dlqName
            };
        }
        catch (Exception ex)
        {

            throw;
        }
    }

    public IAsyncEnumerable<(byte[] Body, Type MessageType, Type HandlerType)> ConsumeAsync(bool autoAck = true
        , CancellationToken cancellationToken = default)
    {
        if (_queueTypeMap == null)
            throw new InvalidOperationException(
                "Queue/message type map is not configured for this event bus instance.");
        return ConsumeAsync(_queueTypeMap
            , autoAck
            , cancellationToken);
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
                        _channel.BasicCancel(consumerTag: tag);

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
            _connection!.Close();

        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RabbitMQ connection CloseAsync failed");
        }

        try
        {
            _connection!.Dispose();

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
            _channel!.Close();

        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RabbitMQ channel CloseAsync failed");
        }

        try
        {
            _channel!.Dispose();

        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RabbitMQ channel DisposeAsync failed");
        }
    }
}

#else
public sealed class RabbitMqEventBus : IEventBus, IAsyncDisposable
{
    private const string XMesssageTtl = "x-message-ttl";
    private readonly ConnectionFactory _factory;
    private readonly ILogger<RabbitMqEventBus> _logger;
    private IConnection? _connection;
    private IChannel? _channel;
    private readonly IDictionary<string, (Type MessageType, Type HandlerType)> _queueTypeMap;
    private readonly List<AsyncEventingBasicConsumer> _consumers = [];
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly EventBusOptions _options;

    private RabbitMqEventBus(string? conn, ILogger<RabbitMqEventBus> logger, IDictionary<string, (Type MessageType, Type HandlerType)> queueTypeMap, EventBusOptions options)
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

    public static async Task<RabbitMqEventBus> CreateAsync(string? conn,
        ILogger<RabbitMqEventBus> logger,
        IDictionary<string, (Type MessageType, Type HandlerType)> queueTypeMap,
        EventBusOptions options,
        CancellationToken cancellationToken = default)
    {
        var bus = new RabbitMqEventBus(conn
            , logger
            , queueTypeMap
            , options);
        await bus
            .ConnectAsync(cancellationToken)
            .ConfigureAwait(false);
        return bus;
    }


    private async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        _connection = await _factory
            .CreateConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        _channel = await _connection
            .CreateChannelAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
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
                await ConnectAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                _channel = await _connection
                    .CreateChannelAsync(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async Task Publish<TEvent>(TEvent @event,
        Type? handlerType = null, //Discard handlerType as it's not used in RabbitMQ
        Guid? sagaId = null,
        CancellationToken cancellationToken = default)
        where TEvent : IEvent
    {
        await EnsureChannelAsync(cancellationToken)
            .ConfigureAwait(false);
        // routingKey equivalent to the exchange name in RabbitMQ terminology
        var exchangeName = MessagingNamingHelper.GetExchangeName(typeof(TEvent)); // event.OrderCreatedEvent

        if (_channel == null)
        {
            throw new InvalidOperationException(
                "Channel is not initialized. Ensure RabbitMqEventBus is properly created.");
        }
        // Declare the exchange (topic) and publish to it. No queue or binding logic here.
        await _channel.ExchangeDeclareAsync(exchange: exchangeName
            , type: ExchangeType.Topic
            , durable: true
            , autoDelete: false
            , arguments: null
            , cancellationToken: cancellationToken);

        sagaId ??= @event.SagaId; // Ensure sagaId is not null, use a new Guid if not provided

        var headers = RabbitMqEventBusHelper.BuildMessageHeaders(@event
            , sagaId
            , typeof(TEvent)
            , Constants.EventTypeHeader);
        var properties = new BasicProperties
        {
            Persistent = true,
            Headers = headers
        };

        var json = JsonHelper.SerializeSafe(@event);
        var body = Encoding.UTF8.GetBytes(json);

        await _channel.BasicPublishAsync(exchange: exchangeName
            , routingKey: exchangeName
            , mandatory: false
            , basicProperties: properties
            , body: body
            , cancellationToken: cancellationToken);
    }

    public async Task Send<TCommand>(TCommand command,
        Type? handlerType = null, //Discard handlerType as it's not used in RabbitMQ
        Guid? sagaId = null,
        CancellationToken cancellationToken = default)
        where TCommand : ICommand

    {
        await EnsureChannelAsync(cancellationToken)
            .ConfigureAwait(false);

        var exchangeName = MessagingNamingHelper.GetExchangeName(typeof(TCommand)); // command.CreateOrderCommand
        var routingKey = MessagingNamingHelper.GetTopicRoutingKey(typeof(TCommand)); // e.g., "command.CreateOrderCommand.#"

        if (_channel == null)
        {
            throw new InvalidOperationException(
                "Channel is not initialized. Ensure RabbitMqEventBus is properly created.");
        }

        await _channel.ExchangeDeclareAsync(
            exchange: exchangeName,
            type: ExchangeType.Direct,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        var headers = RabbitMqEventBusHelper.BuildMessageHeaders(command, sagaId, typeof(TCommand), Constants.CommandTypeHeader);
        var properties = new BasicProperties
        {
            Persistent = true,
            Headers = headers
        };

        var json = JsonHelper.SerializeSafe(command);
        var body = Encoding.UTF8.GetBytes(json);

        await _channel.BasicPublishAsync(
            exchange: exchangeName,
            routingKey: routingKey,
            mandatory: false,
            basicProperties: properties,
            body: body
            , cancellationToken: cancellationToken);
    }

    private async Task PublishToDeadLetterQueueAsync(string dlqName
        , byte[] body
        , IReadOnlyBasicProperties props
        , CancellationToken cancellationToken = default)
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

    private async IAsyncEnumerable<(byte[] Body, Type MessageType, Type HandlerType)> ConsumeAsync(
        IDictionary<string, (Type MessageType, Type HandlerType)> queueTypeMap, bool autoAck = true,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_channel == null)
            throw new InvalidOperationException(
                "Channel is not initialized. Ensure RabbitMqEventBus is properly created.");

        var messageQueue = new ConcurrentQueue<(byte[] Body, Type MessageType, Type HandlerType)>();

        // queueName => e.g. Full format: event.OrderCreatedEvent.CreateOrderSagaHandler.OrderService
        foreach (var kvp in queueTypeMap)
        {
            var queueName = kvp.Key;
            var messageType = kvp.Value.MessageType;
            var handlerType = kvp.Value.HandlerType;

            // Ensure queue and exchange exist and are bound before subscribing the consumer.
            // These operations are idempotent.
            var exchangeName = MessagingNamingHelper.GetExchangeName(messageType); // e.g., "event.OrderCreatedEvent"
            var routingKey = MessagingNamingHelper.GetTopicRoutingKey(messageType); // e.g., "event.OrderCreatedEvent.#"

            var exchangeType = messageType.IsSubclassOf(typeof(EventBase))
                ? ExchangeType.Topic
                : ExchangeType.Direct;

            await _channel.ExchangeDeclareAsync(
                exchange: exchangeName,
                type: exchangeType,
                durable: true,
                autoDelete: false,
                arguments: null,
                cancellationToken: cancellationToken);

            // Declare the queue with DLX and DLQ arguments
            var queueArgs = await DeclareDeadLetter(queueName, cancellationToken);
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

            // Bind queue to exchange with queue name as a routing key
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
                    messageQueue.Enqueue((ea.Body.ToArray(), messageType, handlerType));
                    await Task.CompletedTask;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex
                        , "Failed to process message from queue '{QueueName}' of type '{MessageType}'. Dead-lettering the message"
                        , queueName
                        , messageType.FullName);

                    // DLQ logic:
                    await PublishToDeadLetterQueueAsync(
                        queueName + ".dlq",
                        ea.Body.ToArray(),
                        ea.BasicProperties,
                        cancellationToken);


                }
            };
            await _channel.BasicConsumeAsync(
                queue: queueName,
                autoAck: true,
                consumer: consumer,
                cancellationToken: cancellationToken);

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
        try
        {
            var dlxExchange = $"{queueName}.dlx";
            var dlqName = $"{queueName}.dlq";


            // DLX (Dead Letter Exchange) declare
            await _channel!.ExchangeDeclareAsync(exchange: dlxExchange
                , type: ExchangeType.Direct
                , durable: true
                , autoDelete: false
                , arguments: null
                , cancellationToken: cancellationToken);

            // DLQ (Dead Letter Queue) declare
            await _channel!.QueueDeclareAsync(queue: dlqName
                , durable: true
                , exclusive: false
                , autoDelete: false
                , arguments: null
                , cancellationToken: cancellationToken);

            // DLQ binding
            await _channel!.QueueBindAsync(queue: dlqName
                , exchange: dlxExchange
                , routingKey: dlqName
                , arguments: null
                , cancellationToken: cancellationToken);
            return new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = dlxExchange,
                ["x-dead-letter-routing-key"] = dlqName
            };
        }
        catch (Exception ex)
        {

            throw;
        }
    }

    public IAsyncEnumerable<(byte[] Body, Type MessageType, Type HandlerType)> ConsumeAsync(bool autoAck = true
    , CancellationToken cancellationToken = default)

    {
        if (_queueTypeMap == null)
            throw new InvalidOperationException(
                "Queue/message type map is not configured for this event bus instance.");
        return ConsumeAsync(_queueTypeMap
            , autoAck
            , cancellationToken);
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
                        await _channel.BasicCancelAsync(consumerTag: tag)
                            .ConfigureAwait(false);
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
            await _connection!.CloseAsync()
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RabbitMQ connection CloseAsync failed");
        }

        try
        {
            await _connection!.DisposeAsync()
                .ConfigureAwait(false);
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
            await _channel!.CloseAsync()
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RabbitMQ channel CloseAsync failed");
        }

        try
        {
            await _channel!.DisposeAsync()
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RabbitMQ channel DisposeAsync failed");
        }
    }
}

#endif