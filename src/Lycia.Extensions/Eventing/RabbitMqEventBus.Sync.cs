// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
// All async operations are now cancellation-aware and propagate the CancellationToken for graceful shutdown and responsiveness.

#if NETSTANDARD2_0
using Lycia.Messaging;
using Lycia.Abstractions;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Lycia.Helpers;
using Lycia.Extensions.Configurations;
using Lycia.Extensions.Helpers;
using Lycia;
using Lycia.Extensions;
using Constants = Lycia.Extensions.Configurations.Constants;


namespace Lycia.Extensions.Eventing;

public sealed class RabbitMqEventBus : IEventBus, IDisposable
{
    private const string XMessageTtl = "x-message-ttl";

    private readonly ConnectionFactory _factory;
    private readonly ILogger<RabbitMqEventBus> _logger;
    private IConnection? _connection;
    private IModel? _channel;
    private readonly IDictionary<string, (Type MessageType, Type HandlerType)> _queueTypeMap;
    private readonly List<EventingBasicConsumer> _consumers = [];
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly EventBusOptions _options;
    private readonly IMessageSerializer _serializer;

    private RabbitMqEventBus(
        ILogger<RabbitMqEventBus> logger,
        IDictionary<string, (Type MessageType, Type HandlerType)> queueTypeMap,
        EventBusOptions options,
        IMessageSerializer serializer)
    {
        _logger = logger;
        _queueTypeMap = queueTypeMap;
        _options = options;
        _serializer = serializer ?? throw new InvalidOperationException("IMessageSerializer is null");

        if (options.ConnectionString == null)
            throw new InvalidOperationException("RabbitMqEventBus connection is null");

        _factory = new ConnectionFactory
        {
            Uri = new Uri(options.ConnectionString),
            AutomaticRecoveryEnabled = true
        };
    }

    public static RabbitMqEventBus Create(
        ILogger<RabbitMqEventBus> logger,
        IDictionary<string, (Type MessageType, Type HandlerType)> queueTypeMap,
        EventBusOptions options,
        IMessageSerializer serializer,
        CancellationToken cancellationToken = default)
    {
        var bus = new RabbitMqEventBus(logger, queueTypeMap, options, serializer);
        bus.Connect(cancellationToken);
        return bus;
    }


    private void Connect(CancellationToken cancellationToken = default)
    {
        _connection = _factory.CreateConnection();
        _channel = _connection.CreateModel();
    }


    private void EnsureChannel(CancellationToken cancellationToken = default)
    {
        if (_channel is { IsOpen: true })
            return;

        _connectionLock.Wait(cancellationToken);
        try
        {
            if (_channel is { IsOpen: true })
                return;

            if (_connection is null || !_connection.IsOpen)
            {
                _logger.LogWarning("RabbitMQ connection lost. Reconnecting...");
                Connect(cancellationToken);
            }
            else
            {
                _channel = _connection.CreateModel();
            }
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public void Publish<TEvent>(
        TEvent @event,
        Type? handlerType = null, //Discard handlerType as it's not used in RabbitMQ
        Guid? sagaId = null,
        CancellationToken cancellationToken = default)
        where TEvent : IEvent
    {
        EnsureChannel(cancellationToken);
        // routingKey equivalent to the exchange name in RabbitMQ terminology
        var exchangeName =
            MessagingNamingHelper
                .GetExchangeName(typeof(TEvent)); // event.OrderCreatedEvent or response.OrderCreatedResponse

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

        // Build base headers (Lycia metadata)
        var headers =
            RabbitMqEventBusHelper.BuildMessageHeaders(@event, sagaId, typeof(TEvent), Constants.EventTypeHeader);

        // Ask serializer to produce a body and its own headers (content-type, lycia-type, schema metadata, etc.)
        var (_, serCtx) = _serializer.CreateContextFor(typeof(TEvent));
        var (body, serializerHeaders) = _serializer.Serialize(@event, serCtx);

        // Merge serializer headers into base headers (serializer wins on conflicts)
        foreach (var kv in serializerHeaders)
            headers[kv.Key] = kv.Value;

        var properties = _channel.CreateBasicProperties();
        properties.Persistent = true;
        properties.Headers = headers;

        // Set AMQP ContentType from headers (if provided by the serializer)
        if (serializerHeaders.TryGetValue(_serializer.ContentTypeHeaderKey, out var ctObj)
            && ctObj is string ct && !string.IsNullOrWhiteSpace(ct))
        {
            properties.ContentType = ct;
        }

        _channel.BasicPublish(
            exchange: exchangeName,
            routingKey: exchangeName,
            mandatory: false,
            basicProperties: properties,
            body: body);
    }

    public void Send<TCommand>(
        TCommand command,
        Type? handlerType = null, //Discard handlerType as it's not used in RabbitMQ
        Guid? sagaId = null,
        CancellationToken cancellationToken = default)
        where TCommand : ICommand

    {
        EnsureChannel(cancellationToken);

        var exchangeName = MessagingNamingHelper.GetExchangeName(typeof(TCommand)); // command.CreateOrderCommand
        var routingKey =
            MessagingNamingHelper.GetTopicRoutingKey(typeof(TCommand)); // e.g., "command.CreateOrderCommand.#"

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

        // Build base headers (Lycia metadata)
        var headers =
            RabbitMqEventBusHelper.BuildMessageHeaders(command, sagaId, typeof(TCommand), Constants.CommandTypeHeader);

        // Ask serializer to produce a body and its own headers
        var (_, serCtx) = _serializer.CreateContextFor(typeof(TCommand));
        var (body, serializerHeaders) = _serializer.Serialize(command, serCtx);

        // Merge serializer headers (serializer wins on conflicts)
        foreach (var kv in serializerHeaders)
            headers[kv.Key] = kv.Value;

        var properties = _channel.CreateBasicProperties();
        properties.Persistent = true;
        properties.Headers = headers;

        // Set AMQP ContentType from headers if present
        if (serializerHeaders.TryGetValue(_serializer.ContentTypeHeaderKey, out var ctObj)
            && ctObj is string ct && !string.IsNullOrWhiteSpace(ct))
        {
            properties.ContentType = ct;
        }

        _channel.BasicPublish(
            exchange: exchangeName,
            routingKey: routingKey,
            mandatory: false,
            basicProperties: properties,
            body: body);
    }

    private void PublishToDeadLetterQueue(string dlqName
        , byte[] body
        , IBasicProperties props
        , CancellationToken cancellationToken = default)
    {
        try
        {
            EnsureChannel(cancellationToken);
            if (_channel == null)
                throw new InvalidOperationException("Channel is not initialized for DLQ publish.");

            var dlqArgs = new Dictionary<string, object?>();
            if (_options.MessageTTL is { TotalMilliseconds: > 0 } ttl)
            {
                dlqArgs[XMessageTtl] = (int)ttl.TotalMilliseconds;
            }

            _channel.QueueDeclare(
                queue: dlqName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: dlqArgs.Count > 0 ? dlqArgs : null);

            // For RabbitMQ.Client 7.x+ this is the only valid way:
            var basicProps = props ?? _channel.CreateBasicProperties();
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
                body);

            _logger.LogWarning("Dead-lettered message published to DLQ: {DlqName}", dlqName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish message to dead letter queue: {DlqName}", dlqName);
        }
    }


    public IEnumerable<(byte[] Body, Type MessageType, Type HandlerType, IReadOnlyDictionary<string, object?>
            Headers)>
        Consume(bool autoAck = true, CancellationToken cancellationToken = default)
    {
        if (_queueTypeMap == null)
            throw new InvalidOperationException(
                "Queue/message type map is not configured for this event bus instance.");
        return Consume(_queueTypeMap, autoAck, cancellationToken);
    }


    private IEnumerable<(byte[] Body, Type MessageType, Type HandlerType, IReadOnlyDictionary<string, object?> Headers)> Consume
            (IDictionary<string, (Type MessageType, Type HandlerType)> queueTypeMap, bool autoAck = true,
            CancellationToken cancellationToken = default)
    {
        if (_channel == null)
            throw new InvalidOperationException(
                "Channel is not initialized. Ensure RabbitMqEventBus is properly created.");

        var messageQueue =
            new ConcurrentQueue<(byte[] Body, Type MessageType, Type HandlerType, IReadOnlyDictionary<string, object?>
                Headers)>();

        // queueName => e.g. Full format: event.OrderCreatedEvent.CreateOrderSagaHandler.OrderService
        foreach (var kvp in queueTypeMap)
        {
            var queueName = kvp.Key;
            var messageType = kvp.Value.MessageType;
            var handlerType = kvp.Value.HandlerType;

            // Ensure queue and exchange exist and are bound before subscribing the consumer.
            // These operations are idempotent.
            var exchangeName =
                MessagingNamingHelper
                    .GetExchangeName(
                        messageType); // e.g., "event.OrderCreatedEvent" or "command.CreateOrderCommand" or "response.OrderCreatedResponse"
            var routingKey =
                MessagingNamingHelper
                    .GetTopicRoutingKey(
                        messageType); // e.g., "event.OrderCreatedEvent.#" or "command.CreateOrderCommand.#" or "response.OrderCreatedResponse.#"

            var exchangeType = messageType.IsSubclassOf(typeof(EventBase)) || messageType.IsSubclassOfResponseBase()
                ? ExchangeType.Topic
                : ExchangeType.Direct;

            _channel.ExchangeDeclare(
                exchange: exchangeName,
                type: exchangeType,
                durable: true,
                autoDelete: false);

            // Declare the queue with DLX and DLQ arguments
            var queueArgs = DeclareDeadLetter(queueName, cancellationToken);
            if (_options?.MessageTTL is { TotalMilliseconds: > 0 } ttl)
            {
                queueArgs[XMessageTtl] = (int)ttl.TotalMilliseconds;
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
                    var headers = ea.BasicProperties?.Headers as IReadOnlyDictionary<string, object?>
                                  ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    messageQueue.Enqueue((ea.Body.ToArray(), messageType, handlerType, headers));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex
                        , "Failed to process message from queue '{QueueName}' of type '{MessageType}'. Dead-lettering the message"
                        , queueName
                        , messageType.FullName);

                    // DLQ logic:
                    PublishToDeadLetterQueue(
                        queueName + ".dlq",
                        ea.Body.ToArray(),
                        ea.BasicProperties,
                        cancellationToken);


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
        }
    }

    public IEnumerable<IncomingMessage> ConsumeWithAck(CancellationToken cancellationToken = default)
    {
        EnsureChannel(cancellationToken);

        if (_channel == null)
            throw new InvalidOperationException(
                "Channel is not initialized. Ensure RabbitMqEventBus is properly created.");
        if (_queueTypeMap == null)
            throw new InvalidOperationException(
                "Queue/message type map is not configured for this event bus instance.");

        var messageQueue = new ConcurrentQueue<IncomingMessage>();

        foreach (var kvp in _queueTypeMap)
        {
            var queueName = kvp.Key;
            var messageType = kvp.Value.MessageType;
            var handlerType = kvp.Value.HandlerType;

            var exchangeName = MessagingNamingHelper.GetExchangeName(messageType);
            var routingKey = MessagingNamingHelper.GetTopicRoutingKey(messageType);
            var exchangeType = messageType.IsSubclassOf(typeof(EventBase)) || messageType.IsSubclassOfResponseBase()
                ? ExchangeType.Topic
                : ExchangeType.Direct;

            _channel.ExchangeDeclare(
                exchange: exchangeName,
                type: exchangeType,
                durable: true,
                autoDelete: false,
                arguments: null);

            var queueArgs = DeclareDeadLetter(queueName, cancellationToken);
            if (_options?.MessageTTL is { TotalMilliseconds: > 0 } ttl)
                queueArgs[XMessageTtl] = (int)ttl.TotalMilliseconds;

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
            consumer.Received += (_, ea) =>
            {
                try
                {
                    var headers = ea.BasicProperties?.Headers as IReadOnlyDictionary<string, object?>
                                  ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

                    void Ack() => _channel!.BasicAck(ea.DeliveryTag, multiple: false);

                    void Nack(bool requeue) => _channel!.BasicNack(ea.DeliveryTag, multiple: false,
                        requeue: requeue);

                    messageQueue.Enqueue(new IncomingMessage(ea.Body.ToArray(), messageType, handlerType, headers, Ack, Nack));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Failed to enqueue message for queue '{QueueName}' and type '{MessageType}'", queueName,
                        messageType.FullName);
                    PublishToDeadLetterQueue(queueName + ".dlq", ea.Body.ToArray(), ea.BasicProperties);
                }
            };

            _channel.BasicConsume(queue: queueName, autoAck: false, consumer: consumer);
            _consumers.Add(consumer);
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            while (messageQueue.TryDequeue(out var msg))
                yield return msg;
            Thread.Sleep(50);
        }
    }

    private Dictionary<string, object?> DeclareDeadLetter(string queueName,
        CancellationToken cancellationToken)
    {
        var dlxExchange = $"{queueName}.dlx";
        var dlqName = $"{queueName}.dlq";


        // DLX (Dead Letter Exchange) declare
        _channel!.ExchangeDeclare(
            exchange: dlxExchange,
            type: ExchangeType.Direct,
            durable: true,
            autoDelete: false,
            arguments: null);

        // DLQ (Dead Letter Queue) declare
        _channel!.QueueDeclare(queue: dlqName
            , durable: true
            , exclusive: false
            , autoDelete: false
            , arguments: null);

        // DLQ binding
        _channel!.QueueBind(
            queue: dlqName,
            exchange: dlxExchange,
            routingKey: dlqName,
            arguments: null);

        return new Dictionary<string, object?>
        {
            ["x-dead-letter-exchange"] = dlxExchange,
            ["x-dead-letter-routing-key"] = dlqName
        };
    }

    /// <summary>
    /// Performs application-defined tasks associated with freeing, releasing, or
    /// resetting unmanaged resources asynchronously.</summary>
    /// <returns>A task that represents the asynchronous dispose operation.</returns>
    public void Dispose()
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
                CloseAndDisposeChannel();

                _channel = null;
            }

            if (_connection != null)
            {
                CloseAndDisposeConnection();

                _connection = null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RabbitMQ cleanup failed");
        }
    }

    private void CloseAndDisposeConnection()
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

    private void CloseAndDisposeChannel()
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

#endif