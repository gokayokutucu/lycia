using RabbitMQ.Client;
using System;
using Microsoft.Extensions.Logging; // Assuming ILogger is available via Microsoft.Extensions.DependencyInjection
using Microsoft.Extensions.Logging.Abstractions; // For NullLogger if ILogger isn't configured

namespace Lycia.Infrastructure.RabbitMq
{
    public class RabbitMqConnectionProvider : IRabbitMqChannelProvider
    {
        private readonly IConnection _connection;
        private readonly ILogger<RabbitMqConnectionProvider> _logger;
        private bool _disposed;

        // Default RabbitMQ connection URI. In a real app, this would come from configuration.
        private const string DefaultConnectionUri = "amqp://guest:guest@localhost:5672";

        public RabbitMqConnectionProvider(ILogger<RabbitMqConnectionProvider>? logger, string? connectionUri = null)
        {
            _logger = logger ?? NullLogger<RabbitMqConnectionProvider>.Instance;
            var factory = new ConnectionFactory
            {
                Uri = new Uri(connectionUri ?? DefaultConnectionUri),
                DispatchConsumersAsync = true // Recommended for modern async consumer handling
            };

            try
            {
                _connection = factory.CreateConnection();
                _logger.LogInformation("Successfully connected to RabbitMQ at {ConnectionUri}", connectionUri ?? DefaultConnectionUri);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to connect to RabbitMQ at {ConnectionUri}", connectionUri ?? DefaultConnectionUri);
                throw; // Re-throw to indicate that the provider could not be initialized
            }
        }

        public IModel GetChannel()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }
            
            try
            {
                var channel = _connection.CreateModel();
                _logger.LogDebug("RabbitMQ channel created: {ChannelNumber}", channel.ChannelNumber);
                return channel;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create RabbitMQ channel.");
                throw;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                try
                {
                    _connection?.Close();
                    _connection?.Dispose();
                    _logger.LogInformation("RabbitMQ connection closed and disposed.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error while closing RabbitMQ connection.");
                    // Don't throw from Dispose
                }
            }
            _disposed = true;
        }
    }
}
