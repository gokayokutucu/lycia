// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
// All async operations are now cancellation-aware and propagate the CancellationToken for graceful shutdown and responsiveness.

#if NETSTANDARD2_0
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
    private IModel? _channel;
    // Publishing has its own channel: the only one with publisher confirms enabled, used by exactly one
    // publish at a time (WaitForConfirms waits for every outstanding publish on its channel, and a channel
    // must not be shared by concurrent publishers).
    private IModel? _publishChannel;
    private PublishConfirmTracker? _publishTracker;
    private readonly SemaphoreSlim _publishLock = new(1, 1);
    // Exchanges already declared on the current publish channel, so a steady stream of publishes does not pay
    // an extra round trip each. It is tied to one channel instance and starts empty for every new channel;
    // a channel that is discarded after a failure therefore re-declares, which heals a deleted exchange.
    private IModel? _declaredOnChannel;
    private readonly HashSet<string> _declaredExchanges = new HashSet<string>();
    private readonly IDictionary<string, (Type MessageType, Type HandlerType)> _queueTypeMap;
    private readonly List<EventingBasicConsumer> _consumers = [];
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
        _connection = await Task.Run(() => _factory.CreateConnection(), cancellationToken).ConfigureAwait(false);
        _channel = await Task.Run(() => _connection.CreateModel(), cancellationToken).ConfigureAwait(false);
        _publishChannel = await Task.Run(() => CreatePublishChannel(_connection), cancellationToken).ConfigureAwait(false);
    }

    // Publisher confirms are switched on where the channel is created; the client re-selects them when its
    // automatic recovery re-opens the channel.
    private IModel CreatePublishChannel(IConnection connection)
    {
        var channel = connection.CreateModel();
        if (_options.PublisherConfirms)
        {
            channel.ConfirmSelect();
            _publishTracker = new PublishConfirmTracker(channel);
        }

        return channel;
    }

    private async Task<IModel> EnsurePublishChannelAsync(CancellationToken cancellationToken)
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
                var connection = _connection;
                _publishChannel = await Task.Run(() => CreatePublishChannel(connection), cancellationToken)
                    .ConfigureAwait(false);
            }

            return _publishChannel!;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    /// <summary>
    /// Drops a publish channel whose confirmation state can no longer be trusted, so a confirmation that is
    /// still outstanding for an abandoned publish cannot be mistaken for a later one.
    /// </summary>
    private void DiscardPublishChannel(IModel channel)
    {
        if (ReferenceEquals(_publishChannel, channel)) _publishChannel = null;
        try { channel.Abort(); }
        catch (Exception ex) { _logger.LogDebug(ex, "Aborting a discarded RabbitMQ publish channel failed"); }
        try { channel.Dispose(); }
        catch (Exception ex) { _logger.LogDebug(ex, "Disposing a discarded RabbitMQ publish channel failed"); }
    }

    /// <summary>
    /// Resolves each publish's confirmation from the broker's own <c>basic.ack</c>, <c>basic.nack</c> and
    /// <c>basic.return</c> frames, keyed by the publish sequence number.
    /// </summary>
    /// <remarks>
    /// The client's <c>WaitForConfirms</c> is deliberately not used. It answers with a shared flag that the
    /// ack/nack handler updates from another thread, and under a rapid publish-then-publish sequence it can
    /// report a broker nack as an ack, which here would mean a rejected message recorded as delivered. Only one
    /// publish is ever in flight on the channel, so a single pending slot is enough.
    /// </remarks>
    private sealed class PublishConfirmTracker
    {
        private readonly object _gate = new object();
        private ulong _sequence;
        private TaskCompletionSource<bool>? _pending;
        private bool _returned;

        public PublishConfirmTracker(IModel channel)
        {
            channel.BasicAcks += (_, e) => Resolve(e.DeliveryTag, e.Multiple, acked: true);
            channel.BasicNacks += (_, e) => Resolve(e.DeliveryTag, e.Multiple, acked: false);
            channel.BasicReturn += (_, _) => { lock (_gate) _returned = true; };
            channel.ModelShutdown += (_, e) =>
            {
                lock (_gate) _pending?.TrySetException(new AlreadyClosedException(e));
            };
        }

        /// <summary>Starts tracking the publish that will carry <paramref name="sequence"/>. Call before publishing.</summary>
        public Task<bool> Begin(ulong sequence)
        {
            lock (_gate)
            {
                _sequence = sequence;
                _returned = false;
                _pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                return _pending.Task;
            }
        }

        public bool WasReturned
        {
            get { lock (_gate) return _returned; }
        }

        private void Resolve(ulong deliveryTag, bool multiple, bool acked)
        {
            lock (_gate)
            {
                if (_pending == null) return;
                if (multiple ? deliveryTag >= _sequence : deliveryTag == _sequence)
                    _pending.TrySetResult(acked);
            }
        }
    }

    /// <summary>
    /// Publishes one message on the publish channel and, with publisher confirms enabled, returns only after
    /// RabbitMQ has confirmed it. Declares the exchange first unless <paramref name="exchangeType"/> is null
    /// (the default exchange). Only one publish runs at a time. The confirm wait is bounded by
    /// <see cref="Configurations.EventBusOptions.PublisherConfirmTimeout"/>; on this client generation a
    /// caller's cancellation cannot interrupt an in-flight wait, only prevent one from starting.
    /// </summary>
    private async Task PublishToExchangeAsync(string exchangeName, string? exchangeType, string routingKey,
        Func<IModel, IBasicProperties> buildProperties, byte[] body, bool mandatory, CancellationToken cancellationToken)
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

            if (exchangeType != null && !_declaredExchanges.Contains(exchangeName))
            {
                await Task.Run(() => channel.ExchangeDeclare(exchange: exchangeName, type: exchangeType, durable: true,
                    autoDelete: false, arguments: null), cancellationToken).ConfigureAwait(false);
                _declaredExchanges.Add(exchangeName);
            }

            var properties = buildProperties(channel);
            if (!_options.PublisherConfirms)
            {
                await Task.Run(() => channel.BasicPublish(exchange: exchangeName, routingKey: routingKey,
                    mandatory: mandatory, basicProperties: properties, body: body), cancellationToken).ConfigureAwait(false);
                return;
            }

            try
            {
                var tracker = _publishTracker ?? throw new InvalidOperationException(
                    "The publish channel has no confirmation tracker.");
                await Task.Run(() => PublishAndWaitForConfirm(channel, tracker, exchangeName, routingKey, mandatory,
                    properties, body), cancellationToken).ConfigureAwait(false);
            }
            catch (RabbitMqPublishOutcomeUnknownException)
            {
                DiscardPublishChannel(channel);
                throw;
            }
            catch (Exception ex) when (ex is not RabbitMqPublishException && ex is not OperationCanceledException)
            {
                DiscardPublishChannel(channel);
                throw new RabbitMqPublishOutcomeUnknownException(exchangeName, routingKey,
                    $"the publish failed after it was sent ({ex.GetType().Name}: {ex.Message}).", ex);
            }
        }
        finally
        {
            _publishLock.Release();
        }
    }

    private void PublishAndWaitForConfirm(IModel channel, PublishConfirmTracker tracker, string exchangeName,
        string routingKey, bool mandatory, IBasicProperties properties, byte[] body)
    {
        // The sequence number the broker will confirm is known before the frame is sent, so the tracker is armed
        // first and an ack can never arrive unobserved.
        var confirmation = tracker.Begin(channel.NextPublishSeqNo);
        channel.BasicPublish(exchange: exchangeName, routingKey: routingKey, mandatory: mandatory,
            basicProperties: properties, body: body);

        if (!confirmation.Wait(_options.PublisherConfirmTimeout))
            throw new RabbitMqPublishOutcomeUnknownException(exchangeName, routingKey,
                $"no confirmation arrived within {_options.PublisherConfirmTimeout}.");

        // A mandatory message that no queue receives is returned (basic.return) BEFORE the broker acks it, so
        // the ack alone cannot be read as delivery.
        if (tracker.WasReturned) throw new RabbitMqUnroutableMessageException(exchangeName, routingKey);
        if (!confirmation.Result) throw new RabbitMqPublishNackedException(exchangeName, routingKey);
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
                _channel = await Task.Run(() => _connection.CreateModel(), cancellationToken).ConfigureAwait(false);
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

        IBasicProperties BuildProperties(IModel channel)
        {
            var properties = channel.CreateBasicProperties();
            properties.Persistent = true;
            properties.Headers = headers;
            ApplyRequestProperties(properties, message);

            // Set AMQP ContentType from headers (if provided by the serializer)
            if (serializerHeaders.TryGetValue(_serializer.ContentTypeHeaderKey, out var ctObj)
                && ctObj is string ct && !string.IsNullOrWhiteSpace(ct))
            {
                properties.ContentType = ct;
            }

            return properties;
        }

        await PublishToExchangeAsync(exchangeName, exchangeType, routingKey, BuildProperties, body,
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

        IBasicProperties BuildProperties(IModel channel)
        {
            var properties = channel.CreateBasicProperties();
            properties.Persistent = true;
            properties.Headers = headers;
            ApplyRequestProperties(properties, command);

            // Set AMQP ContentType from headers if present
            if (serializerHeaders.TryGetValue(_serializer.ContentTypeHeaderKey, out var ctObj)
                && ctObj is string ct && !string.IsNullOrWhiteSpace(ct))
            {
                properties.ContentType = ct;
            }

            return properties;
        }

        await PublishToExchangeAsync(exchangeName, ExchangeType.Direct, routingKey, BuildProperties, body,
            RequiresRoute(PublishKind.Command), cancellationToken).ConfigureAwait(false);
    }

    private async Task PublishToDeadLetterQueueAsync(string dlqName, byte[] body, IBasicProperties props,
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

            await Task.Run(() => _channel.QueueDeclare(
                queue: dlqName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: dlqArgs.Count > 0 ? dlqArgs : null)
            , cancellationToken);

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

            await PublishToExchangeAsync(string.Empty, null, dlqName, _ => basicProps, body, mandatory: false,
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

                await Task.Run(() => _channel.ExchangeDeclare(
                    exchange: exchangeName,
                    type: exchangeType,
                    durable: true,
                    autoDelete: false,
                    arguments: null)
                , cancellationToken);

                // Declare the queue with DLX and DLQ arguments
                var queueArgs = await DeclareDeadLetter(queueName, cancellationToken);
                if (_options?.MessageTTL is { TotalMilliseconds: > 0 } ttl)
                {
                    queueArgs[XMessageTtl] = (int)ttl.TotalMilliseconds;
                }

                await Task.Run(() => _channel.QueueDeclare(
                    queue: queueName,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: queueArgs.Count > 0 ? queueArgs : null)
                , cancellationToken);

                // Bind queue to exchange with queue name as a routing key
                await Task.Run(() => _channel.QueueBind(
                    queue: queueName,
                    exchange: exchangeName,
                    routingKey: routingKey,
                    arguments: null)
                , cancellationToken);

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
                        _logger.LogError(ex,
                            "Failed to process message from queue '{QueueName}' of type '{MessageType}'. Dead-lettering the message",
                            queueName, messageType.FullName);
                        // DLQ logic:
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await PublishToDeadLetterQueueAsync(queueName + ".dlq", ea.Body.ToArray(),
                                    ea.BasicProperties).ConfigureAwait(false);
                            }
                            catch (Exception dlqEx)
                            {
                                _logger.LogError(dlqEx, "Failed to publish to DLQ for queue '{QueueName}'", queueName);
                            }
                        });
                    }
                };

                await Task.Run(() => _channel.BasicConsume(
                    queue: queueName,
                    autoAck: autoAck,
                    consumer: consumer)
                , cancellationToken);

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

                await Task.Run(() => _channel.ExchangeDeclare(
                    exchange: exchangeName,
                    type: exchangeType,
                    durable: true,
                    autoDelete: false,
                    arguments: null)
                , cancellationToken);

                var queueArgs = await DeclareDeadLetter(queueName, cancellationToken).ConfigureAwait(false);
                if (_options?.MessageTTL is { TotalMilliseconds: > 0 } ttl)
                    queueArgs[XMessageTtl] = (int)ttl.TotalMilliseconds;

                await Task.Run(() => _channel.QueueDeclare(
                    queue: queueName,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: queueArgs.Count > 0 ? queueArgs : null)
                , cancellationToken);

                // Bind queue to exchange with queue name as a routing key
                await Task.Run(() => _channel.QueueBind(
                    queue: queueName,
                    exchange: exchangeName,
                    routingKey: routingKey,
                    arguments: null)
                , cancellationToken);

                var consumer = new EventingBasicConsumer(_channel);
                consumer.Received += (_, ea) =>
                {
                    try
                    {
                        var headers = ea.BasicProperties?.Headers as IReadOnlyDictionary<string, object?>
                                      ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

                        ValueTask Ack()
                        {
                            _channel!.BasicAck(ea.DeliveryTag, multiple: false);
                            return default;
                        }

                        ValueTask Nack(bool requeue)
                        {
                            _channel!.BasicNack(ea.DeliveryTag, multiple: false, requeue);
                            return default;
                        }

                        queue.Enqueue(new IncomingMessage(ea.Body.ToArray(), messageType, handlerType, headers, Ack, Nack));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "Failed to enqueue message for queue '{QueueName}' and type '{MessageType}'", queueName,
                            messageType.FullName);
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await PublishToDeadLetterQueueAsync(queueName + ".dlq", ea.Body.ToArray(),
                                    ea.BasicProperties, default).ConfigureAwait(false);
                            }
                            catch (Exception dlqEx)
                            {
                                _logger.LogError(dlqEx, "Failed to publish to DLQ for queue '{QueueName}'", queueName);
                            }
                        });
                    }
                };

                await Task.Run(() => _channel.BasicConsume(queue: queueName, autoAck: false, consumer: consumer), cancellationToken);
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

            try
            {
                await Task.Delay(50, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task<Dictionary<string, object?>> DeclareDeadLetter(string queueName,
        CancellationToken cancellationToken)
    {
        var dlxExchange = $"{queueName}.dlx";
        var dlqName = $"{queueName}.dlq";


        // DLX (Dead Letter Exchange) declare
        await Task.Run(() => _channel!.ExchangeDeclare(
            exchange: dlxExchange,
            type: ExchangeType.Direct,
            durable: true,
            autoDelete: false,
            arguments: null)
        , cancellationToken);

        // DLQ (Dead Letter Queue) declare
        await Task.Run(() => _channel!.QueueDeclare(
            queue: dlqName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null)
        , cancellationToken);

        // DLQ binding
        await Task.Run(() => _channel!.QueueBind(
            queue: dlqName,
            exchange: dlxExchange,
            routingKey: dlqName,
            arguments: null)
        , cancellationToken);

        return new Dictionary<string, object?>
        {
            ["x-dead-letter-exchange"] = dlxExchange,
            ["x-dead-letter-routing-key"] = dlqName
        };
    }

    private static void ApplyRequestProperties(IBasicProperties properties, object message)
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
                        await Task.Run(() => _channel.BasicCancel(consumerTag: tag)).ConfigureAwait(false);
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
                var publishChannel = _publishChannel;
                try { await Task.Run(() => publishChannel.Close()).ConfigureAwait(false); }
                catch (Exception ex) { _logger.LogWarning(ex, "RabbitMQ publish channel Close failed"); }
                try { await Task.Run(() => publishChannel.Dispose()).ConfigureAwait(false); }
                catch (Exception ex) { _logger.LogWarning(ex, "RabbitMQ publish channel Dispose failed"); }
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
            await Task.Run(() => _connection!.Close()).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RabbitMQ connection CloseAsync failed");
        }

        try
        {
            await Task.Run(() => _connection!.Dispose()).ConfigureAwait(false);
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
            await Task.Run(() => _channel!.Close()).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RabbitMQ channel CloseAsync failed");
        }

        try
        {
            await Task.Run(() => _channel!.Dispose()).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RabbitMQ channel DisposeAsync failed");
        }
    }
}
#endif
