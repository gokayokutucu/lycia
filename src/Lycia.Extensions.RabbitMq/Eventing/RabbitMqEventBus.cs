// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
// All async operations are now cancellation-aware and propagate the CancellationToken for graceful shutdown and responsiveness.

#if NET8_0_OR_GREATER
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Lycia.Common.Messaging;
using Lycia.Extensions.Configurations;
using Lycia.Extensions.Helpers;
using Lycia.Helpers;
using Lycia.Messaging;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Messaging;
using Lycia.Saga.Abstractions.Serializers;
using Lycia.Saga.Abstractions.Scheduling;
using Lycia.Saga.Messaging;
using Lycia.Saga.Extensions;
using Constants = Lycia.Extensions.Configurations.Constants;

namespace Lycia.Extensions.Eventing;

/// <summary>Implements Lycia's at-least-once command, event, and targeted-response transport over RabbitMQ.</summary>
public sealed partial class RabbitMqEventBus : IEventBus, INativeSchedulingTransport, ISchedulingResourceManager, IAsyncDisposable
{
    private const string XMessageTtl = "x-message-ttl";

    private readonly ConnectionFactory _factory;
    private readonly ILogger<RabbitMqEventBus> _logger;
    private IConnection? _connection;
    private IChannel? _channel;
    // Publishing has its own channel. It is the only one created with publisher confirms, and it is used by
    // exactly one publish at a time (a channel must not be shared by concurrent publishers), so consuming,
    // topology and acknowledgements on _channel never contend with a publish that is waiting for a confirm.
    private IChannel? _publishChannel;
    private readonly SemaphoreSlim _publishLock = new(1, 1);
    // Exchanges already declared on the current publish channel, so a steady stream of publishes does not pay
    // an extra round trip each. It is tied to one channel instance and starts empty for every new channel;
    // a channel that is discarded after a failure therefore re-declares, which heals a deleted exchange.
    private IChannel? _declaredOnChannel;
    private readonly HashSet<string> _declaredExchanges = [];
    private readonly IDictionary<string, (Type MessageType, Type HandlerType)> _queueTypeMap;
    private readonly List<AsyncEventingBasicConsumer> _consumers = [];
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly EventBusOptions _options;
    private readonly IMessageSerializer _serializer;

    /// <inheritdoc />
    public string ApplicationId { get; }

    private readonly TaskCompletionSource<bool> _consumersReady =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Completes when a consume loop has declared every mapped queue, bound it to its exchange,
    /// and registered its consumer. Awaiting this before publishing removes the race in which a
    /// message reaches a direct exchange before the consumer binding exists and is dropped.
    /// Faults if consumer registration fails so awaiters do not wait forever.
    /// </summary>
    public Task ConsumerReady => _consumersReady.Task;

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
        ValidateConfirmOptions(options);

        if (options.ConnectionString == null)
            throw new InvalidOperationException("RabbitMqEventBus connection is null");
        ApplicationId = EndpointIdentityNormalizer.Default.Normalize(options.ApplicationId!);

        _factory = new ConnectionFactory
        {
            Uri = new Uri(options.ConnectionString),
            AutomaticRecoveryEnabled = true
        };
    }

    /// <summary>Creates, connects, and returns a RabbitMQ event-bus instance for the supplied topology map.</summary>
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
        _publishChannel = await CreatePublishChannelAsync(_connection, cancellationToken).ConfigureAwait(false);
    }

    // Publisher confirms are switched on where the channel is created, so a channel recreated after a lost
    // connection cannot silently come back without them.
    private Task<IChannel> CreatePublishChannelAsync(IConnection connection, CancellationToken cancellationToken) =>
        connection.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: _options.PublisherConfirms,
                publisherConfirmationTrackingEnabled: _options.PublisherConfirms),
            cancellationToken);

    private async Task<IChannel> EnsurePublishChannelAsync(CancellationToken cancellationToken)
    {
        if (_publishChannel is { IsOpen: true } open)
            return open;

        await _connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_publishChannel is { IsOpen: true } recovered)
                return recovered;

            if (_connection is null || !_connection.IsOpen)
            {
                _logger.LogWarning("RabbitMQ connection lost. Reconnecting...");
                await ConnectAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                _publishChannel = await CreatePublishChannelAsync(_connection, cancellationToken).ConfigureAwait(false);
            }

            return _publishChannel!;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    /// <summary>
    /// Drops a publish channel whose confirmation state can no longer be trusted (a confirm timed out, was
    /// cancelled, or the connection died mid-publish). Leaving it in place would let an outstanding
    /// confirmation from the abandoned publish be mistaken for a later one.
    /// </summary>
    private async Task DiscardPublishChannelAsync(IChannel channel)
    {
        if (ReferenceEquals(_publishChannel, channel)) _publishChannel = null;
        try
        {
            using var closeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await channel.CloseAsync(200, "publish channel discarded", abort: true, closeTimeout.Token)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Closing a discarded RabbitMQ publish channel failed");
        }

        try
        {
            await channel.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Disposing a discarded RabbitMQ publish channel failed");
        }
    }

    /// <summary>
    /// Publishes one message on the publish channel and, with publisher confirms enabled, returns only after
    /// RabbitMQ has confirmed it. Declares the exchange first unless <paramref name="exchangeType"/> is null
    /// (the default exchange). Only one publish runs at a time.
    /// </summary>
    private async Task PublishToExchangeAsync(string exchangeName, string? exchangeType, string routingKey,
        BasicProperties properties, byte[] body, bool mandatory, CancellationToken cancellationToken)
    {
        await _publishLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // A connection that cannot be established fails here, before anything is published: a definite
            // failure, distinct from the unknown outcome of a publish that was already sent.
            var channel = await EnsurePublishChannelAsync(cancellationToken).ConfigureAwait(false);

            if (!ReferenceEquals(_declaredOnChannel, channel))
            {
                _declaredOnChannel = channel;
                _declaredExchanges.Clear();
            }

            // With confirms enabled the declaration and the publish share one time budget.
            using var confirmTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (_options.PublisherConfirms) confirmTimeout.CancelAfter(_options.PublisherConfirmTimeout);
            var token = _options.PublisherConfirms ? confirmTimeout.Token : cancellationToken;

            if (exchangeType != null && !_declaredExchanges.Contains(exchangeName))
            {
                try
                {
                    await channel.ExchangeDeclareAsync(exchange: exchangeName, type: exchangeType, durable: true,
                        autoDelete: false, arguments: null, cancellationToken: token).ConfigureAwait(false);
                    _declaredExchanges.Add(exchangeName);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Nothing was published, so this is a definite failure rather than an unknown outcome.
                    await DiscardPublishChannelAsync(channel).ConfigureAwait(false);
                    throw new TimeoutException(
                        $"RabbitMQ did not answer the declaration of exchange '{exchangeName}' within " +
                        $"{_options.PublisherConfirmTimeout}; nothing was published.");
                }
            }

            if (!_options.PublisherConfirms)
            {
                await channel.BasicPublishAsync(exchange: exchangeName, routingKey: routingKey, mandatory: mandatory,
                    basicProperties: properties, body: body, cancellationToken: cancellationToken).ConfigureAwait(false);
                return;
            }

            try
            {
                // With confirmation tracking enabled this completes only when RabbitMQ has confirmed the
                // publish; a nack or a returned mandatory message surfaces as a PublishException.
                await channel.BasicPublishAsync(exchange: exchangeName, routingKey: routingKey, mandatory: mandatory,
                    basicProperties: properties, body: body, cancellationToken: confirmTimeout.Token).ConfigureAwait(false);
            }
            catch (PublishException ex) when (ex.IsReturn)
            {
                throw new RabbitMqUnroutableMessageException(exchangeName, routingKey, ex);
            }
            catch (PublishException ex)
            {
                throw new RabbitMqPublishNackedException(exchangeName, routingKey, ex);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The caller gave up. The message may be on the broker, but it is certainly not confirmed.
                await DiscardPublishChannelAsync(channel).ConfigureAwait(false);
                throw;
            }
            catch (OperationCanceledException ex)
            {
                await DiscardPublishChannelAsync(channel).ConfigureAwait(false);
                throw new RabbitMqPublishOutcomeUnknownException(exchangeName, routingKey,
                    $"no confirmation arrived within {_options.PublisherConfirmTimeout}.", ex);
            }
            catch (Exception ex) when (ex is not RabbitMqPublishException)
            {
                await DiscardPublishChannelAsync(channel).ConfigureAwait(false);
                throw new RabbitMqPublishOutcomeUnknownException(exchangeName, routingKey,
                    $"the publish failed after it was sent ({ex.GetType().Name}: {ex.Message}).", ex);
            }
        }
        finally
        {
            _publishLock.Release();
        }
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

    /// <inheritdoc />
    public async Task Publish<TEvent>(
        TEvent @event,
        Type? handlerType = null, //Discard handlerType as it's not used in RabbitMQ
        Guid? sagaId = null,
        CancellationToken cancellationToken = default)
        where TEvent : IEvent
    {
        if (@event is IResponse)
            throw new InvalidOperationException(
                $"Response '{@event.GetType().FullName}' cannot be published. Use Respond(request, response)." );
        await PublishMessageAsync(@event, typeof(TEvent), sagaId, PublishKind.Event, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task Respond<TRequest, TResponse>(TRequest request, TResponse response, Type? handlerType = null,
        Guid? sagaId = null, CancellationToken cancellationToken = default)
        where TRequest : IMessage
        where TResponse : IResponse<TRequest>
    {
        var endpoint = RequestRouting.RequireResponseEndpoint(request, response);
        response.PrepareResponse(request, sagaId ?? request.SagaId ?? Guid.Empty, endpoint);
        return PublishMessageAsync(response, response.GetType(), sagaId, PublishKind.Response, cancellationToken);
    }

    private async Task PublishMessageAsync(
        IMessage message,
        Type messageType,
        Guid? sagaId,
        PublishKind kind,
        CancellationToken cancellationToken)
    {
        // routingKey equivalent to the exchange name in RabbitMQ terminology
        var exchangeName =
            MessagingNamingHelper
                .GetExchangeName(messageType);
        var exchangeType = RabbitMqTopology.GetExchangeType(messageType);
        var routingKey = RabbitMqTopology.GetPublishKey(message, messageType);

        // Build base headers (Lycia metadata)
        var headers =
            RabbitMqEventBusHelper.BuildMessageHeaders(message, sagaId, messageType, Constants.EventTypeHeader);

        // Inject current Activity context into headers for downstream consumers
        Observability.LyciaTracePropagation.Inject(headers);

        // Ask serializer to produce a body and its own headers (content-type, lycia-type, schema metadata, etc.)
        var (_, serCtx) = _serializer.CreateContextFor(messageType);
        var (body, serializerHeaders) = _serializer.Serialize(message, serCtx);

        // Merge serializer headers into base headers (serializer wins on conflicts)
        foreach (var kv in serializerHeaders)
            headers[kv.Key] = kv.Value;

        var properties = new BasicProperties
        {
            Persistent = true,
            Headers = headers
        };
        ApplyRequestProperties(properties, message);

        // Set AMQP ContentType from headers (if provided by the serializer)
        if (serializerHeaders.TryGetValue(_serializer.ContentTypeHeaderKey, out var ctObj)
            && ctObj is string ct && !string.IsNullOrWhiteSpace(ct))
        {
            properties.ContentType = ct;
        }

        await PublishToExchangeAsync(exchangeName, exchangeType, routingKey, properties, body,
            RequiresRoute(kind), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task Send<TCommand>(
        TCommand command,
        Type? handlerType = null, //Discard handlerType as it's not used in RabbitMQ
        Guid? sagaId = null,
        CancellationToken cancellationToken = default) where TCommand : ICommand
    {
        RequestRouting.Prepare(command);

        var exchangeName = MessagingNamingHelper.GetExchangeName(typeof(TCommand)); // command.CreateOrderCommand
        var routingKey = MessagingNamingHelper.GetCommandRoutingKey(typeof(TCommand));

        // Build base headers (Lycia metadata)
        var headers =
            RabbitMqEventBusHelper.BuildMessageHeaders(command, sagaId, typeof(TCommand), Constants.CommandTypeHeader);

        // Inject current Activity context into headers for downstream consumers
        Observability.LyciaTracePropagation.Inject(headers);

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
        ApplyRequestProperties(properties, command);

        // Set AMQP ContentType from headers if present
        if (serializerHeaders.TryGetValue(_serializer.ContentTypeHeaderKey, out var ctObj)
            && ctObj is string ct && !string.IsNullOrWhiteSpace(ct))
        {
            properties.ContentType = ct;
        }

        await PublishToExchangeAsync(exchangeName, ExchangeType.Direct, routingKey, properties, body,
            RequiresRoute(PublishKind.Command), cancellationToken).ConfigureAwait(false);
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

            await PublishToExchangeAsync(string.Empty, null, dlqName, basicProps, body, mandatory: false,
                cancellationToken).ConfigureAwait(false);

            _logger.LogWarning("Dead-lettered message published to DLQ: {DlqName}", dlqName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish message to dead letter queue: {DlqName}", dlqName);
        }
    }


    /// <inheritdoc />
    public IAsyncEnumerable<(byte[] Body, Type MessageType, Type HandlerType, IReadOnlyDictionary<string, object?>
            Headers)>
        ConsumeAsync(bool autoAck = true, CancellationToken cancellationToken = default)
    {
        if (_queueTypeMap == null)
            throw new InvalidOperationException(
                "Queue/message type map is not configured for this event bus instance.");
        return ConsumeAsync(_queueTypeMap, autoAck, cancellationToken);
    }


    private async
        IAsyncEnumerable<(byte[] Body, Type MessageType, Type HandlerType, IReadOnlyDictionary<string, object?> Headers
            )> ConsumeAsync(
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
        try
        {
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
                var routingKey = RabbitMqTopology.GetBindingKey(messageType, ApplicationId);
                var exchangeType = RabbitMqTopology.GetExchangeType(messageType);

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
        }
        catch (Exception ex)
        {
            // Fault the readiness signal so callers synchronizing on ConsumerReady
            // fail fast instead of waiting forever when registration fails.
            _consumersReady.TrySetException(ex);
            throw;
        }

        _consumersReady.TrySetResult(true);

        while (!cancellationToken.IsCancellationRequested)
        {
            while (messageQueue.TryDequeue(out var result))
                yield return result;

            await Task.Delay(50, cancellationToken);
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<IncomingMessage> ConsumeWithAckAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_channel == null)
            throw new InvalidOperationException(
                "Channel is not initialized. Ensure RabbitMqEventBus is properly created.");
        if (_queueTypeMap == null)
            throw new InvalidOperationException(
                "Queue/message type map is not configured for this event bus instance.");

        var queue = new ConcurrentQueue<IncomingMessage>();

        try
        {
            foreach (var kvp in _queueTypeMap)
            {
                var queueName = kvp.Key;
                var messageType = kvp.Value.MessageType;
                var handlerType = kvp.Value.HandlerType;

                var exchangeName = MessagingNamingHelper.GetExchangeName(messageType);
                var routingKey = RabbitMqTopology.GetBindingKey(messageType, ApplicationId);
                var exchangeType = RabbitMqTopology.GetExchangeType(messageType);

                await _channel.ExchangeDeclareAsync(
                    exchange: exchangeName,
                    type: exchangeType,
                    durable: true,
                    autoDelete: false,
                    arguments: null, cancellationToken: cancellationToken);

                var queueArgs = await DeclareDeadLetter(queueName, cancellationToken).ConfigureAwait(false);
                if (_options?.MessageTTL is { TotalMilliseconds: > 0 } ttl)
                    queueArgs[XMessageTtl] = (int)ttl.TotalMilliseconds;

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
                consumer.ReceivedAsync += async (_, ea) =>
                {
                    try
                    {
                        var headers = ea.BasicProperties?.Headers as IReadOnlyDictionary<string, object?>
                                      ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

                        ValueTask Ack() => _channel!.BasicAckAsync(ea.DeliveryTag, multiple: false,
                            cancellationToken: cancellationToken);

                        ValueTask Nack(bool requeue) => _channel!.BasicNackAsync(ea.DeliveryTag, multiple: false,
                            requeue: requeue, cancellationToken: cancellationToken);

                        queue.Enqueue(new IncomingMessage(ea.Body.ToArray(), messageType, handlerType, headers, Ack, Nack));
                        await Task.CompletedTask;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "Failed to enqueue message for queue '{QueueName}' and type '{MessageType}'", queueName,
                            messageType.FullName);
                        await PublishToDeadLetterQueueAsync(queueName + ".dlq", ea.Body.ToArray(), ea.BasicProperties,
                            cancellationToken).ConfigureAwait(false);
                    }
                };

                await _channel.BasicConsumeAsync(queue: queueName, autoAck: false, consumer: consumer,
                    cancellationToken: cancellationToken);
                _consumers.Add(consumer);
            }
        }
        catch (Exception ex)
        {
            // Fault the readiness signal so callers synchronizing on ConsumerReady
            // fail fast instead of waiting forever when registration fails.
            _consumersReady.TrySetException(ex);
            throw;
        }

        _consumersReady.TrySetResult(true);

        while (!cancellationToken.IsCancellationRequested)
        {
            while (queue.TryDequeue(out var msg))
                yield return msg;

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

    private static void ApplyRequestProperties(BasicProperties properties, object message)
    {
        if (!(message is IRequestRoutingMetadata metadata)) return;
        properties.CorrelationId = metadata.RequestId == Guid.Empty ? null : metadata.RequestId.ToString();
        properties.ReplyTo = metadata.ResponseEndpoint;
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

            if (_publishChannel != null)
            {
                try { await _publishChannel.CloseAsync().ConfigureAwait(false); }
                catch (Exception ex) { _logger.LogWarning(ex, "RabbitMQ publish channel CloseAsync failed"); }
                try { await _publishChannel.DisposeAsync().ConfigureAwait(false); }
                catch (Exception ex) { _logger.LogWarning(ex, "RabbitMQ publish channel DisposeAsync failed"); }
                _publishChannel = null;
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
#endif
