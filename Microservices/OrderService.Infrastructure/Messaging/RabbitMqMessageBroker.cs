using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using OrderService.Application.Contracts.Infrastructure;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions; // For specific RabbitMQ exceptions

namespace OrderService.Infrastructure.Messaging
{
    public class RabbitMqMessageBroker : IMessageBroker, IDisposable
    {
        private readonly RabbitMqOptions _options;
        private IConnection _connection;
        private IModel _channel;
        private readonly object _lock = new object(); // For thread-safe connection/channel creation

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
                            VirtualHost = _options.VirtualHost,
                            DispatchConsumersAsync = true // Recommended for async consumers
                        };

                        try
                        {
                            _connection = factory.CreateConnection();
                            _channel = _connection.CreateModel();

                            // Declare the exchange (idempotent)
                            _channel.ExchangeDeclare(exchange: _options.ExchangeName, type: ExchangeType.Topic, durable: true);
                            
                            Console.WriteLine($"RabbitMQ connected to {_options.Hostname} and channel/exchange '{_options.ExchangeName}' established.");
                        }
                        catch (BrokerUnreachableException ex)
                        {
                            Console.WriteLine($"RabbitMQ connection failed: {ex.Message}. Check RabbitMQ server is running and accessible.");
                            // Handle this appropriately - retry logic, circuit breaker, or rethrow
                            throw; // Rethrow for now, higher level should handle startup issues
                        }
                    }
                }
            }
            if (_channel == null || _channel.IsClosed) // Ensure channel is also open
            {
                 lock(_lock)
                 {
                    if(_channel == null || _channel.IsClosed)
                    {
                        _channel = _connection?.CreateModel();
                        if(_channel != null) {
                             _channel.ExchangeDeclare(exchange: _options.ExchangeName, type: ExchangeType.Topic, durable: true);
                        } else {
                             Console.WriteLine($"RabbitMQ channel could not be created. Connection might be null.");
                             throw new InvalidOperationException("RabbitMQ channel could not be created.");
                        }
                    }
                 }
            }
        }

        public Task PublishAsync<T>(T message, CancellationToken cancellationToken = default) where T : class
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            // Ensure connection/channel is alive, attempt to reconnect if not.
            Connect();
            if (_channel == null || !_channel.IsOpen)
            {
                // Log critical error, cannot publish
                Console.WriteLine("RabbitMQ channel is not open. Cannot publish message.");
                throw new InvalidOperationException("RabbitMQ channel is not available.");
            }

            try
            {
                var messageType = typeof(T).Name; // Basic routing key by type name
                // In a real app, you might have more sophisticated routing key generation
                // or get it from message attributes/configuration.
                var routingKey = $"orders.{messageType.ToLowerInvariant()}"; 

                var jsonMessage = JsonSerializer.Serialize(message);
                var body = Encoding.UTF8.GetBytes(jsonMessage);

                var properties = _channel.CreateBasicProperties();
                properties.Persistent = true; // Make messages persistent
                properties.ContentType = "application/json";
                properties.Type = messageType; // Set message type for consumers
                properties.MessageId = Guid.NewGuid().ToString(); // Unique message ID
                properties.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

                _channel.BasicPublish(
                    exchange: _options.ExchangeName,
                    routingKey: routingKey,
                    basicProperties: properties,
                    body: body);
                
                Console.WriteLine($"Published {messageType} to {_options.ExchangeName} with routing key {routingKey}");
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                // Log error
                Console.WriteLine($"Error publishing message: {ex.Message}");
                // Handle publish error - retry, dead-letter, etc.
                throw; // Rethrow for now
            }
        }

        public void Dispose()
        {
            try
            {
                _channel?.Close();
                _channel?.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error closing RabbitMQ channel: {ex.Message}");
            }

            try
            {
                _connection?.Close();
                _connection?.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error closing RabbitMQ connection: {ex.Message}");
            }
        }
    }
}
