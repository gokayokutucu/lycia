using Lycia.Messaging;
using Lycia.Saga.Abstractions;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using System.Text;
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

        if (_connection is null || !_connection.IsOpen)
        {
            _logger.LogWarning("RabbitMQ connection lost. Reconnecting...");
            await ConnectAsync().ConfigureAwait(false);
            return;
        }

        _channel = await _connection.CreateChannelAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_channel != null)
            {
                await _channel.CloseAsync().ConfigureAwait(false);
                await _channel.DisposeAsync().ConfigureAwait(false);
            }

            if (_connection != null)
            {
                await _connection.CloseAsync().ConfigureAwait(false);
                await _connection.DisposeAsync().ConfigureAwait(false);
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

        var properties = new BasicProperties
        {
            Persistent = true,
            Headers = sagaId.HasValue
                ? new Dictionary<string, object?> { { "SagaId", sagaId.Value.ToString() } }
                : null
        };

        var json = JsonConvert.SerializeObject(@event);
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

        var properties = new BasicProperties
        {
            Persistent = true,
            Headers = sagaId.HasValue
                ? new Dictionary<string, object?> { { "SagaId", sagaId.Value.ToString() } }
                : null
        };

        var json = JsonConvert.SerializeObject(command);
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

        foreach (var kvp in queueTypeMap)
        {
            var queueName = kvp.Key;
            var messageType = kvp.Value;

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
