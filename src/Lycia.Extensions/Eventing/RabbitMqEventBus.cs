// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
// All async operations are now cancellation-aware and propagate the CancellationToken for graceful shutdown and responsiveness.

using Lycia.Messaging;
using Lycia.Saga.Abstractions;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Lycia.Saga.Helpers;
using Lycia.Extensions.Configurations;
using Lycia.Extensions.Helpers;
using Lycia.Saga.Extensions;
using Constants = Lycia.Extensions.Configurations.Constants;

namespace Lycia.Extensions.Eventing;

public sealed class RabbitMqEventBus : IEventBus, IAsyncDisposable
{
    private const string XMessageTtl = "x-message-ttl";

    private readonly ConnectionFactory _factory;
    private readonly ILogger<RabbitMqEventBus> _logger;
    private IConnection? _connection;
    private IChannel? _channel;
    private readonly IDictionary<string, (Type MessageType, Type HandlerType)> _queueTypeMap;
    private readonly List<AsyncEventingBasicConsumer> _consumers = [];
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

        if (options.ConnectionString == null) throw new InvalidOperationException("RabbitMqEventBus connection is null");

        _factory = new ConnectionFactory
        {
            Uri = new Uri(options.ConnectionString),
            AutomaticRecoveryEnabled = true
        };
    }

    public static async Task<RabbitMqEventBus> CreateAsync(
        ILogger<RabbitMqEventBus> logger,
        IDictionary<string, (Type MessageType, Type HandlerType)> queueTypeMap,
        EventBusOptions options,
        IMessageSerializer serializer,
        CancellationToken cancellationToken = default)
    {
        var bus = new RabbitMqEventBus(logger, queueTypeMap, options, serializer);
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

    public async Task Publish<TEvent>(
        TEvent @event,
        Type? handlerType = null, //Discard handlerType as it's not used in RabbitMQ
        Guid? sagaId = null,
        CancellationToken cancellationToken = default)
        where TEvent : IEvent
    {
        await EnsureChannelAsync(cancellationToken).ConfigureAwait(false);
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
        await _channel.ExchangeDeclareAsync(
            exchange: exchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            arguments: null, cancellationToken: cancellationToken);

        // Build base headers (Lycia metadata)
        var headers =
            RabbitMqEventBusHelper.BuildMessageHeaders(@event, sagaId, typeof(TEvent), Constants.EventTypeHeader);

        // Ask serializer to produce a body and its own headers (content-type, lycia-type, schema metadata, etc.)
        var (_, serCtx) = _serializer.CreateContextFor(typeof(TEvent));
        var (body, serializerHeaders) = _serializer.Serialize(@event, serCtx);

        // Merge serializer headers into base headers (serializer wins on conflicts)
        foreach (var kv in serializerHeaders)
            headers[kv.Key] = kv.Value;

        var properties = new BasicProperties
        {
            Persistent = true,
            Headers = headers
        };

        // Set AMQP ContentType from headers (if provided by the serializer)
        if (serializerHeaders.TryGetValue(_serializer.ContentTypeHeaderKey, out var ctObj)
            && ctObj is string ct && !string.IsNullOrWhiteSpace(ct))
        {
            properties.ContentType = ct;
        }

        await _channel.BasicPublishAsync(
            exchange: exchangeName,
            routingKey: exchangeName,
            mandatory: false,
            basicProperties: properties,
            body: body,
            cancellationToken: cancellationToken);
    }

    public async Task Send<TCommand>(
        TCommand command,
        Type? handlerType = null, //Discard handlerType as it's not used in RabbitMQ
        Guid? sagaId = null,
        CancellationToken cancellationToken = default) where TCommand : ICommand
    {
        await EnsureChannelAsync(cancellationToken).ConfigureAwait(false);

        var exchangeName = MessagingNamingHelper.GetExchangeName(typeof(TCommand)); // command.CreateOrderCommand
        var routingKey =
            MessagingNamingHelper.GetTopicRoutingKey(typeof(TCommand)); // e.g., "command.CreateOrderCommand.#"

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

        // Build base headers (Lycia metadata)
        var headers =
            RabbitMqEventBusHelper.BuildMessageHeaders(command, sagaId, typeof(TCommand), Constants.CommandTypeHeader);

        // Ask serializer to produce a body and its own headers
        var (_, serCtx) = _serializer.CreateContextFor(typeof(TCommand));
        var (body, serializerHeaders) = _serializer.Serialize(command, serCtx);

        // Merge serializer headers (serializer wins on conflicts)
        foreach (var kv in serializerHeaders)
            headers[kv.Key] = kv.Value;

        var properties = new BasicProperties
        {
            Persistent = true,
            Headers = headers
        };

        // Set AMQP ContentType from headers if present
        if (serializerHeaders.TryGetValue(_serializer.ContentTypeHeaderKey, out var ctObj)
            && ctObj is string ct && !string.IsNullOrWhiteSpace(ct))
        {
            properties.ContentType = ct;
        }

        await _channel.BasicPublishAsync(
            exchange: exchangeName,
            routingKey: routingKey,
            mandatory: false,
            basicProperties: properties,
            body: body,
            cancellationToken: cancellationToken);
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
                dlqArgs[XMessageTtl] = (int)ttl.TotalMilliseconds;
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

    private async
        IAsyncEnumerable<(byte[] Body, Type MessageType, Type HandlerType, IReadOnlyDictionary<string, object?> Headers)> ConsumeAsync(
            IDictionary<string, (Type MessageType, Type HandlerType)> queueTypeMap, bool autoAck = true,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
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

            await _channel.ExchangeDeclareAsync(
                exchange: exchangeName,
                type: exchangeType,
                durable: true,
                autoDelete: false,
                arguments: null, cancellationToken: cancellationToken);

            // Declare the queue with DLX and DLQ arguments
            var queueArgs = await DeclareDeadLetter(queueName, cancellationToken);
            if (_options?.MessageTTL is { TotalMilliseconds: > 0 } ttl)
            {
                queueArgs[XMessageTtl] = (int)ttl.TotalMilliseconds;
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
                    var headers = ea.BasicProperties?.Headers as IReadOnlyDictionary<string, object?>
                                  ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    messageQueue.Enqueue((ea.Body.ToArray(), messageType, handlerType, headers));
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
                autoAck: autoAck,
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

    private async Task<Dictionary<string, object?>> DeclareDeadLetter(string queueName,
        CancellationToken cancellationToken)
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

    public IAsyncEnumerable<(byte[] Body, Type MessageType, Type HandlerType, IReadOnlyDictionary<string, object?> Headers)> 
        ConsumeAsync(bool autoAck = true, CancellationToken cancellationToken = default)
    {
        if (_queueTypeMap == null)
            throw new InvalidOperationException(
                "Queue/message type map is not configured for this event bus instance.");
        return ConsumeAsync(_queueTypeMap, autoAck, cancellationToken);
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