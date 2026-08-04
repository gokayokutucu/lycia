using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Lycia.Common.Messaging;
using Lycia.Messaging;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Messaging;
using Lycia.Saga.Abstractions.Serializers;
using Lycia.Saga.Extensions;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Net;

namespace Lycia.Extensions.Nats;

/// <summary>
/// Lycia NATS transport. JetStream is the default for durable saga delivery; Core NATS is an
/// explicitly selected ephemeral mode for workloads that tolerate subscriber absence.
/// </summary>
public sealed class NatsEventBus : IEventBus, IAsyncDisposable
{
    private readonly IDictionary<string, (Type MessageType, Type HandlerType)> _queueTypeMap;
    private readonly NatsEventBusOptions _options;
    private readonly IMessageSerializer _serializer;
    private readonly NatsClient _client;
    private readonly INatsJSContext _jetStream;
    private readonly SemaphoreSlim _streamLock = new(1, 1);
    private bool _streamReady;

    /// <inheritdoc />
    public string ApplicationId { get; }

    /// <summary>Creates a NATS transport for the discovered logical subscriptions.</summary>
    public NatsEventBus(
        IDictionary<string, (Type MessageType, Type HandlerType)> queueTypeMap,
        NatsEventBusOptions options,
        IMessageSerializer serializer)
    {
        _queueTypeMap = queueTypeMap ?? throw new ArgumentNullException(nameof(queueTypeMap));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        if (string.IsNullOrWhiteSpace(options.ApplicationId))
            throw new ArgumentException("ApplicationId is required.", nameof(options));
        ApplicationId = EndpointIdentityNormalizer.Default.Normalize(options.ApplicationId);

        _client = new NatsClient(options.Url, $"Lycia.{options.ApplicationId}");
        _jetStream = _client.CreateJetStreamContext();
    }

    /// <inheritdoc />
    public async Task Send<TCommand>(TCommand command, Type? handlerType = null, Guid? sagaId = null,
        CancellationToken cancellationToken = default) where TCommand : ICommand
    {
        RequestRouting.Prepare(command);
        await PublishMessageAsync(command, sagaId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task Respond<TRequest, TResponse>(TRequest request, TResponse response, Type? handlerType = null,
        Guid? sagaId = null, CancellationToken cancellationToken = default)
        where TRequest : IMessage
        where TResponse : IResponse<TRequest>
    {
        var endpoint = response.ResponseEndpoint
                       ?? (request as IRequestRoutingMetadata)?.ResponseEndpoint
                       ?? ApplicationId;
        response.PrepareResponse(request, sagaId ?? request.SagaId ?? Guid.Empty, endpoint);
        return PublishMessageAsync(response, sagaId, cancellationToken);
    }

    /// <inheritdoc />
    public Task Publish<TEvent>(TEvent @event, Type? handlerType = null, Guid? sagaId = null,
        CancellationToken cancellationToken = default) where TEvent : IEvent
    {
        if (@event is IResponse)
            throw new InvalidOperationException(
                $"Response '{@event.GetType().FullName}' cannot be published. Use Respond(request, response)." );
        return PublishMessageAsync(@event, sagaId, cancellationToken);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<(byte[] Body, Type MessageType, Type HandlerType, IReadOnlyDictionary<string, object?> Headers)>
        ConsumeAsync(bool autoAck = true, CancellationToken cancellationToken = default) =>
        ConsumeWithoutAckAsync(autoAck, cancellationToken);

    /// <inheritdoc />
    public async IAsyncEnumerable<IncomingMessage> ConsumeWithAckAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var consumerToken = lifetime.Token;
        var queue = new ConcurrentQueue<IncomingMessage>();
        if (_options.UseJetStream) await EnsureStreamAsync(consumerToken).ConfigureAwait(false);

        var workers = _queueTypeMap.Select(pair => _options.UseJetStream
            ? ConsumeJetStreamAsync(pair.Key, pair.Value, queue, consumerToken)
            : ConsumeCoreAsync(pair.Key, pair.Value, queue, consumerToken)).ToArray();

        try
        {
            while (!consumerToken.IsCancellationRequested)
            {
                while (queue.TryDequeue(out var message)) yield return message;
                await Task.Delay(20, consumerToken).ConfigureAwait(false);
            }
        }
        finally
        {
            lifetime.Cancel();
            try { await Task.WhenAll(workers).ConfigureAwait(false); }
            catch (OperationCanceledException) when (consumerToken.IsCancellationRequested) { }
        }
    }

    private async Task PublishMessageAsync(IMessage message, Guid? sagaId, CancellationToken cancellationToken)
    {
        var messageType = message.GetType();
        var subject = NatsTopology.GetPublishSubject(message, messageType);
        var (body, headers) = Serialize(message, messageType, sagaId);
        if (_options.UseJetStream)
        {
            await EnsureStreamAsync(cancellationToken).ConfigureAwait(false);
            var ack = await _jetStream.PublishAsync(subject, body, NatsRawSerializer<byte[]>.Default,
                headers: headers, cancellationToken: cancellationToken).ConfigureAwait(false);
            ack.EnsureSuccess();
            return;
        }

        await _client.PublishAsync(subject, body, headers, serializer: NatsRawSerializer<byte[]>.Default,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task ConsumeJetStreamAsync(
        string queueName,
        (Type MessageType, Type HandlerType) registration,
        ConcurrentQueue<IncomingMessage> output,
        CancellationToken cancellationToken)
    {
        var subject = NatsTopology.GetSubscriptionSubject(registration.MessageType, ApplicationId);
        var config = new ConsumerConfig(NatsTopology.GetConsumerName(queueName))
        {
            FilterSubject = subject,
            AckWait = _options.AckWait,
            MaxDeliver = _options.MaxDeliver,
            AckPolicy = ConsumerConfigAckPolicy.Explicit
        };
        var consumer = await _jetStream.CreateOrUpdateConsumerAsync(_options.StreamName, config, cancellationToken)
            .ConfigureAwait(false);

        await foreach (var message in consumer
                           .ConsumeAsync<byte[]>(NatsRawSerializer<byte[]>.Default, cancellationToken: cancellationToken)
                           .WithCancellation(cancellationToken))
        {
            message.EnsureSuccess();
            var captured = message;
            output.Enqueue(new IncomingMessage(
                captured.Data ?? Array.Empty<byte>(),
                registration.MessageType,
                registration.HandlerType,
                ToHeaders(captured.Headers),
                () => captured.AckAsync(cancellationToken: CancellationToken.None),
                requeue => requeue
                    ? captured.NakAsync(cancellationToken: CancellationToken.None)
                    : captured.AckTerminateAsync(cancellationToken: CancellationToken.None)));
        }
    }

    private async Task ConsumeCoreAsync(
        string queueName,
        (Type MessageType, Type HandlerType) registration,
        ConcurrentQueue<IncomingMessage> output,
        CancellationToken cancellationToken)
    {
        var subject = NatsTopology.GetSubscriptionSubject(registration.MessageType, ApplicationId);
        await foreach (var message in _client.SubscribeAsync<byte[]>(subject,
                           NatsTopology.GetQueueGroup(queueName), NatsRawSerializer<byte[]>.Default,
                           cancellationToken: cancellationToken).WithCancellation(cancellationToken))
        {
            message.EnsureSuccess();
            output.Enqueue(new IncomingMessage(
                message.Data ?? Array.Empty<byte>(), registration.MessageType, registration.HandlerType,
                ToHeaders(message.Headers), () => default, _ => default));
        }
    }

    private async IAsyncEnumerable<(byte[] Body, Type MessageType, Type HandlerType, IReadOnlyDictionary<string, object?> Headers)>
        ConsumeWithoutAckAsync(bool autoAck, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var message in ConsumeWithAckAsync(cancellationToken).WithCancellation(cancellationToken))
        {
            if (autoAck) await message.Ack().ConfigureAwait(false);
            yield return (message.Body, message.MessageType, message.HandlerType, message.Headers);
        }
    }

    private async Task EnsureStreamAsync(CancellationToken cancellationToken)
    {
        if (_streamReady) return;
        await _streamLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_streamReady) return;
            await _jetStream.CreateOrUpdateStreamAsync(new StreamConfig(_options.StreamName,
                new[] { "command.>", "event.>", "response.>" }), cancellationToken).ConfigureAwait(false);
            _streamReady = true;
        }
        finally
        {
            _streamLock.Release();
        }
    }

    private (byte[] Body, NatsHeaders Headers) Serialize(IMessage message, Type messageType, Guid? sagaId)
    {
        var headers = new NatsHeaders();
        AddMetadata(headers, message, sagaId);
        var (_, context) = _serializer.CreateContextFor(messageType);
        var (body, serializerHeaders) = _serializer.Serialize(message, context);
        foreach (var pair in serializerHeaders) headers[pair.Key] = pair.Value?.ToString() ?? string.Empty;
        return (body, headers);
    }

    private static void AddMetadata(NatsHeaders headers, IMessage message, Guid? sagaId)
    {
        headers["MessageId"] = message.MessageId.ToString();
        headers["CorrelationId"] = message.CorrelationId.ToString();
        headers["ParentMessageId"] = message.ParentMessageId.ToString();
        headers["CausationId"] = message.CausationId?.ToString() ?? string.Empty;
        headers["SagaId"] = (message.SagaId ?? sagaId)?.ToString() ?? string.Empty;
        headers["ApplicationId"] = message.ApplicationId;
        if (message is IRequestRoutingMetadata routing)
        {
            headers["RequestId"] = routing.RequestId.ToString();
            headers["ResponseEndpoint"] = routing.ResponseEndpoint ?? string.Empty;
            headers["ReplyTo"] = routing.ResponseEndpoint ?? string.Empty;
        }
    }

    private static IReadOnlyDictionary<string, object?> ToHeaders(NatsHeaders? headers)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (headers == null) return result;
        foreach (var pair in headers) result[pair.Key] = pair.Value.ToString();
        return result;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _client.DisposeAsync();
}
