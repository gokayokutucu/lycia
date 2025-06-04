using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using OrderService.Application.Contracts.Infrastructure; // For IMessageBroker
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using Lycia.Saga.Abstractions; // For IEventBus
using Lycia.Messaging; // For IMessage, ICommand, IEvent

namespace OrderService.Infrastructure.Messaging
{
    public class RabbitMqMessageBroker : IMessageBroker, IEventBus, IDisposable
    {
        private readonly RabbitMqOptions _options;
        private IConnection _connection;
        private IModel _channel;
        private readonly object _lock = new object();

        public RabbitMqMessageBroker(IOptions<RabbitMqOptions> options)
        {
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            Connect();
        }

        private void Connect()
        {
            if (_connection == null || !_connection.IsOpen)
            {
                lock (_lock)
                {
                    if (_connection == null || !_connection.IsOpen)
                    {
                        var factory = new ConnectionFactory()
                        {
                            HostName = _options.Hostname,
                            Port = _options.Port,
                            UserName = _options.Username,
                            Password = _options.Password,
                            VirtualHost = _options.VirtualHost
                        };

                        try
                        {
                            _connection = factory.CreateConnection();
                            _channel = _connection.CreateModel();
                            _channel.ExchangeDeclare(exchange: _options.ExchangeName, type: ExchangeType.Topic, durable: true);
                            Console.WriteLine($"RabbitMQ connected to {_options.Hostname} and channel/exchange '{_options.ExchangeName}' established.");
                        }
                        catch (BrokerUnreachableException ex)
                        {
                            Console.WriteLine($"RabbitMQ connection failed: {ex.Message}. Check RabbitMQ server.");
                            throw;
                        }
                    }
                }
            }
            if (_channel == null || _channel.IsClosed)
            {
                 lock(_lock)
                 {
                    if(_channel == null || _channel.IsClosed)
                    {
                        if (_connection == null || !_connection.IsOpen)
                        {
                             Console.WriteLine($"RabbitMQ connection is not open. Attempting to reconnect to recreate channel.");
                             _connection?.Dispose();
                             _connection = null;
                             Connect();
                             if(_channel == null || _channel.IsClosed) {
                                 throw new InvalidOperationException("RabbitMQ channel could not be created after reconnect attempt.");
                             }
                        } else {
                            _channel = _connection.CreateModel();
                            _channel.ExchangeDeclare(exchange: _options.ExchangeName, type: ExchangeType.Topic, durable: true);
                             Console.WriteLine($"RabbitMQ channel recreated for exchange '{_options.ExchangeName}'.");
                        }
                    }
                 }
            }
        }

        public Task PublishAsync<T>(T messagePayload, CancellationToken cancellationToken = default) where T : class
        {
            if (messagePayload == null) throw new ArgumentNullException(nameof(messagePayload));

            Connect();
            if (_channel == null || !_channel.IsOpen) throw new InvalidOperationException("RabbitMQ channel not available for IMessageBroker.PublishAsync.");

            try
            {
                var messageType = typeof(T).Name;
                var routingKey = $"generic.{messageType.ToLowerInvariant()}";

                var jsonMessage = JsonSerializer.Serialize(messagePayload);
                var body = Encoding.UTF8.GetBytes(jsonMessage);

                var properties = _channel.CreateBasicProperties();
                properties.Persistent = true;
                properties.ContentType = "application/json";
                properties.Type = messageType;
                properties.MessageId = Guid.NewGuid().ToString();
                // CorrelationId is not part of generic messagePayload for IMessageBroker
                properties.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()); // Corrected

                _channel.BasicPublish(
                    exchange: _options.ExchangeName,
                    routingKey: routingKey,
                    mandatory: false,
                    basicProperties: properties,
                    body: body);
                
                Console.WriteLine($"SUCCESS (IMessageBroker): Published {messageType} to exchange '{_options.ExchangeName}' with routing key '{routingKey}'");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL (IMessageBroker): Error publishing message type {typeof(T).Name}: {ex.Message}");
                throw;
            }
            return Task.CompletedTask;
        }

        Task IEventBus.Publish<TEvent>(TEvent @event, Guid? sagaId)
        {
            if (@event == null) throw new ArgumentNullException(nameof(@event));
            if (!(@event is IMessage lyciaMessage))
                throw new ArgumentException("Event must be an IMessage type for Lycia IEventBus.", nameof(@event));

            Connect();
            if (_channel == null || !_channel.IsOpen) throw new InvalidOperationException("RabbitMQ channel not available for IEventBus.Publish.");

            try
            {
                var messageType = @event.GetType().Name;
                var routingKey = messageType;

                var jsonMessage = JsonSerializer.Serialize(@event, @event.GetType());
                var body = Encoding.UTF8.GetBytes(jsonMessage);

                var properties = _channel.CreateBasicProperties();
                properties.Persistent = true;
                properties.ContentType = "application/json";
                properties.Type = messageType;
                properties.MessageId = lyciaMessage.MessageId.ToString();
                properties.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()); // Corrected

                Guid? finalSagaId = sagaId ?? lyciaMessage.SagaId;
                if (finalSagaId.HasValue)
                {
                    properties.Headers ??= new Dictionary<string, object>();
                    properties.Headers["x-saga-id"] = finalSagaId.Value.ToString();
                }

                 _channel.BasicPublish(
                    exchange: _options.ExchangeName,
                    routingKey: routingKey,
                    mandatory: false,
                    basicProperties: properties,
                    body: body);

                Console.WriteLine($"SUCCESS (IEventBus.Publish): Published {messageType} to exchange '{_options.ExchangeName}' with routing key '{routingKey}' (SagaId: {finalSagaId?.ToString() ?? "N/A"})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL (IEventBus.Publish): Error publishing event type {@event.GetType().Name}: {ex.Message}");
                throw;
            }
            return Task.CompletedTask;
        }

        Task IEventBus.Send<TCommand>(TCommand command, Guid? sagaId)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (!(command is IMessage lyciaMessage))
                throw new ArgumentException("Command must be an IMessage type for Lycia IEventBus.", nameof(command));

            Connect();
            if (_channel == null || !_channel.IsOpen) throw new InvalidOperationException("RabbitMQ channel not available for IEventBus.Send.");

            try
            {
                var messageType = command.GetType().Name;
                var routingKey = messageType;

                var jsonMessage = JsonSerializer.Serialize(command, command.GetType());
                var body = Encoding.UTF8.GetBytes(jsonMessage);

                var properties = _channel.CreateBasicProperties();
                properties.Persistent = true;
                properties.ContentType = "application/json";
                properties.Type = messageType;
                properties.MessageId = lyciaMessage.MessageId.ToString();
                properties.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()); // Corrected

                Guid? finalSagaId = sagaId ?? lyciaMessage.SagaId;
                if (finalSagaId.HasValue)
                {
                     properties.Headers ??= new Dictionary<string, object>();
                    properties.Headers["x-saga-id"] = finalSagaId.Value.ToString();
                }

                _channel.BasicPublish(
                    exchange: _options.ExchangeName,
                    routingKey: routingKey,
                    mandatory: false,
                    basicProperties: properties,
                    body: body);
                Console.WriteLine($"SUCCESS (IEventBus.Send): Sent {messageType} to exchange '{_options.ExchangeName}' with routing key '{routingKey}' (SagaId: {finalSagaId?.ToString() ?? "N/A"})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL (IEventBus.Send): Error sending command type {command.GetType().Name}: {ex.Message}");
                throw;
            }
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _channel?.Dispose();
            _connection?.Dispose();
        }
    }
}
