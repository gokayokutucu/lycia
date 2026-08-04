using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Lycia.Common.Messaging;
using Lycia.Messaging;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Messaging;
using Lycia.Saga.Abstractions.Serializers;
using Lycia.Saga.Extensions;

namespace Lycia.Extensions.Kafka;

/// <summary>
/// Lycia Kafka transport with one logical group per command owner or event subscription.
/// Offsets are committed only after the listener acknowledges successful processing.
/// </summary>
public sealed class KafkaEventBus : IEventBus, IAsyncDisposable
{
    private readonly IDictionary<string, (Type MessageType, Type HandlerType)> _queueTypeMap;
    private readonly KafkaEventBusOptions _options;
    private readonly IMessageSerializer _serializer;
    private readonly IProducer<string, byte[]> _producer;
    private readonly IAdminClient _admin;
    private readonly ConcurrentDictionary<string, Task> _topicTasks = new(StringComparer.Ordinal);
    private readonly List<IConsumer<string, byte[]>> _consumers = new();

    /// <inheritdoc />
    public string ApplicationId { get; }

    /// <summary>Creates a Kafka transport for the discovered logical subscriptions.</summary>
    public KafkaEventBus(
        IDictionary<string, (Type MessageType, Type HandlerType)> queueTypeMap,
        KafkaEventBusOptions options,
        IMessageSerializer serializer)
    {
        _queueTypeMap = queueTypeMap ?? throw new ArgumentNullException(nameof(queueTypeMap));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        if (string.IsNullOrWhiteSpace(options.ApplicationId))
            throw new ArgumentException("ApplicationId is required.", nameof(options));
        ApplicationId = EndpointIdentityNormalizer.Default.Normalize(options.ApplicationId);

        _producer = new ProducerBuilder<string, byte[]>(new ProducerConfig
        {
            BootstrapServers = options.BootstrapServers,
            EnableIdempotence = true,
            Acks = Acks.All
        }).Build();
        _admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = options.BootstrapServers }).Build();
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
        var endpoint = RequestRouting.RequireResponseEndpoint(request, response);
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
        var output = new ConcurrentQueue<IncomingMessage>();
        foreach (var pair in _queueTypeMap)
        {
            var topic = KafkaTopology.GetSubscriptionTopic(
                _options.TopicPrefix, pair.Value.MessageType, ApplicationId);
            await EnsureTopicAsync(topic, consumerToken).ConfigureAwait(false);
        }

        var workers = _queueTypeMap.Select(pair =>
            ConsumeRegistrationAsync(pair.Key, pair.Value, output, consumerToken)).ToArray();

        try
        {
            while (!consumerToken.IsCancellationRequested)
            {
                while (output.TryDequeue(out var message)) yield return message;
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
        var type = message.GetType();
        var topic = KafkaTopology.GetPublishTopic(_options.TopicPrefix, message, type);
        await EnsureTopicAsync(topic, cancellationToken).ConfigureAwait(false);
        var (body, headers) = Serialize(message, type, sagaId);
        await _producer.ProduceAsync(topic, new Message<string, byte[]>
        {
            Key = KafkaTopology.GetPartitionKey(message),
            Value = body,
            Headers = headers
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task ConsumeRegistrationAsync(
        string queueName,
        (Type MessageType, Type HandlerType) registration,
        ConcurrentQueue<IncomingMessage> output,
        CancellationToken cancellationToken)
    {
        // Confluent's Consume call is synchronous. Yield before entering its poll loop so discovery can
        // start every logical subscription instead of blocking on the first registration.
        await Task.Yield();
        var topic = KafkaTopology.GetSubscriptionTopic(
            _options.TopicPrefix, registration.MessageType, ApplicationId);
        var config = new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = KafkaTopology.GetConsumerGroup(_options.TopicPrefix, queueName),
            EnableAutoCommit = false,
            EnableAutoOffsetStore = false,
            AutoOffsetReset = _options.AutoOffsetReset == AutoOffsetReset.Earliest
                ? Confluent.Kafka.AutoOffsetReset.Earliest
                : Confluent.Kafka.AutoOffsetReset.Latest
        };
        using var consumer = new ConsumerBuilder<string, byte[]>(config).Build();
        lock (_consumers) _consumers.Add(consumer);
        consumer.Subscribe(topic);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                ConsumeResult<string, byte[]>? record;
                try { record = consumer.Consume(TimeSpan.FromMilliseconds(250)); }
                catch (ConsumeException) { throw; }
                if (record == null)
                {
                    await Task.Yield();
                    continue;
                }

                var completion = new TaskCompletionSource<AckDecision>();
                output.Enqueue(new IncomingMessage(
                    record.Message.Value ?? Array.Empty<byte>(),
                    registration.MessageType,
                    registration.HandlerType,
                    ToHeaders(record.Message.Headers),
                    () => Complete(completion, AckDecision.Commit),
                    requeue => Complete(completion, requeue ? AckDecision.Requeue : AckDecision.Drop)));

                using var cancellationRegistration = cancellationToken.Register(
                    () => completion.TrySetCanceled());
                var decision = await completion.Task.ConfigureAwait(false);
                if (decision == AckDecision.Requeue)
                    consumer.Seek(record.TopicPartitionOffset);
                else
                    consumer.Commit(record);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        finally
        {
            consumer.Close();
            lock (_consumers) _consumers.Remove(consumer);
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

    private Task EnsureTopicAsync(string topic, CancellationToken cancellationToken)
    {
        if (!_options.EnsureTopics) return Task.CompletedTask;
        return _topicTasks.GetOrAdd(topic, _ => CreateTopicAsync(topic, cancellationToken));
    }

    private async Task CreateTopicAsync(string topic, CancellationToken cancellationToken)
    {
        try
        {
            await _admin.CreateTopicsAsync(new[]
            {
                new TopicSpecification
                {
                    Name = topic,
                    NumPartitions = _options.NumPartitions,
                    ReplicationFactor = _options.ReplicationFactor
                }
            }).ConfigureAwait(false);
        }
        catch (CreateTopicsException exception) when (
            exception.Results.All(result => result.Error.Code == ErrorCode.TopicAlreadyExists))
        {
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    private (byte[] Body, Headers Headers) Serialize(IMessage message, Type type, Guid? sagaId)
    {
        var (_, context) = _serializer.CreateContextFor(type);
        var (body, serializerHeaders) = _serializer.Serialize(message, context);
        var headers = new Headers();
        AddHeader(headers, "MessageId", message.MessageId.ToString());
        AddHeader(headers, "CorrelationId", message.CorrelationId.ToString());
        AddHeader(headers, "ParentMessageId", message.ParentMessageId.ToString());
        AddHeader(headers, "CausationId", message.CausationId?.ToString());
        AddHeader(headers, "SagaId", (message.SagaId ?? sagaId)?.ToString());
        AddHeader(headers, "ApplicationId", message.ApplicationId);
        if (message is IRequestRoutingMetadata routing)
        {
            AddHeader(headers, "RequestId", routing.RequestId.ToString());
            AddHeader(headers, "ResponseEndpoint", routing.ResponseEndpoint);
            AddHeader(headers, "ReplyTo", routing.ResponseEndpoint);
        }
        foreach (var pair in serializerHeaders) AddHeader(headers, pair.Key, pair.Value?.ToString());
        return (body, headers);
    }

    private static void AddHeader(Headers headers, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) headers.Add(key, Encoding.UTF8.GetBytes(value));
    }

    private static IReadOnlyDictionary<string, object?> ToHeaders(Headers? headers)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (headers == null) return result;
        foreach (var header in headers) result[header.Key] = Encoding.UTF8.GetString(header.GetValueBytes());
        return result;
    }

    private static ValueTask Complete(TaskCompletionSource<AckDecision> completion, AckDecision decision)
    {
        completion.TrySetResult(decision);
        return default;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        lock (_consumers)
            foreach (var consumer in _consumers.ToArray()) consumer.Close();
        _producer.Flush(TimeSpan.FromSeconds(10));
        _producer.Dispose();
        _admin.Dispose();
        return default;
    }

    private enum AckDecision { Commit, Requeue, Drop }
}
