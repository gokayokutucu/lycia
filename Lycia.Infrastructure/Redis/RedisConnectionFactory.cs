using StackExchange.Redis;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lycia.Infrastructure.Redis
{
    public class RedisConnectionFactory : IRedisConnectionFactory
    {
        private readonly string _connectionString;
        private readonly ILogger<RedisConnectionFactory> _logger;
        private readonly Lazy<Task<IConnectionMultiplexer>> _lazyConnection;
        private bool _disposed;

        public RedisConnectionFactory(string connectionString, ILogger<RedisConnectionFactory>? logger)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _logger = logger ?? NullLogger<RedisConnectionFactory>.Instance;

            _lazyConnection = new Lazy<Task<IConnectionMultiplexer>>(async () =>
            {
                _logger.LogInformation("Attempting to connect to Redis at {RedisConnectionString}", ObscureConnectionString(_connectionString));
                try
                {
                    // ConfigurationOptions can be used for more detailed setup if needed
                    var connection = await ConnectionMultiplexer.ConnectAsync(_connectionString);
                    _logger.LogInformation("Successfully connected to Redis at {RedisConnectionString}", ObscureConnectionString(_connectionString));

                    connection.ConnectionFailed += (sender, args) => {
                        _logger.LogError(args.Exception, "Redis connection failed. Endpoint: {Endpoint}, FailureType: {FailureType}", args.EndPoint, args.FailureType);
                    };
                    connection.ConnectionRestored += (sender, args) => {
                        _logger.LogInformation("Redis connection restored. Endpoint: {Endpoint}, FailureType: {FailureType}", args.EndPoint, args.FailureType);
                    };
                    connection.ErrorMessage += (sender, args) => {
                        _logger.LogError("Redis error message: {Message}", args.Message);
                    };
                    connection.InternalError += (sender, args) => {
                        _logger.LogError(args.Exception, "Redis internal error. Origin: {Origin}", args.Origin);
                    };

                    return connection;
                }
                catch (Exception ex)
                {
                    _logger.LogCritical(ex, "Failed to establish initial connection to Redis at {RedisConnectionString}", ObscureConnectionString(_connectionString));
                    throw; // Re-throw to indicate connection failure on first attempt
                }
            });
        }

        private string ObscureConnectionString(string? cs)
        {
            if (string.IsNullOrEmpty(cs)) return "N/A";
            try
            {
                var parts = cs.Split(',');
                if (parts.Length > 0 && parts[0].Contains("@"))
                {
                    var serverPart = parts[0].Substring(parts[0].IndexOf('@') + 1);
                    return $"****@{serverPart},...";
                }
                return cs; // Or a more generic obscuring logic
            }
            catch
            {
                return "ErrorObscuringConnectionString";
            }
        }
        
        private async Task<IConnectionMultiplexer> GetConnectedMultiplexerAsync()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(RedisConnectionFactory));
            }
            // This will either return the existing task or start the connection attempt.
            // If the initial connection failed, this will re-throw the exception from the Lazy<T> factory.
            return await _lazyConnection.Value;
        }

        public IDatabase GetDatabase(int db = -1)
        {
            // Blocking call for simplicity in GetDatabase, but connection itself is async.
            // Consider making GetDatabase async if preferred.
            var multiplexer = GetConnectedMultiplexerAsync().GetAwaiter().GetResult();
            if (!multiplexer.IsConnected)
            {
                _logger.LogWarning("Redis connection is not currently active for connection string {RedisConnectionString}. Attempting to get database instance anyway.", ObscureConnectionString(_connectionString));
            }
            _logger.LogDebug("Returning Redis database instance for connection {RedisConnectionString} and database {DatabaseId}.", ObscureConnectionString(_connectionString), db);
            return multiplexer.GetDatabase(db);
        }
        
        public IConnectionMultiplexer GetConnectionMultiplexer()
        {
            _logger.LogDebug("Returning Redis connection multiplexer for connection {RedisConnectionString}.", ObscureConnectionString(_connectionString));
            return GetConnectedMultiplexerAsync().GetAwaiter().GetResult();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            _logger.LogDebug("Disposing RedisConnectionFactory for connection {RedisConnectionString}.", ObscureConnectionString(_connectionString));
            if (disposing)
            {
                if (_lazyConnection.IsValueCreated)
                {
                    try
                    {
                        var connectionTask = _lazyConnection.Value;
                        if (connectionTask.IsCompletedSuccessfully)
                        {
                            connectionTask.Result.Close(); // Close gracefully
                            connectionTask.Result.Dispose(); // Dispose
                            _logger.LogInformation("Redis connection multiplexer closed and disposed.");
                        }
                        else if (connectionTask.IsFaulted)
                        {
                             _logger.LogWarning("Redis connection multiplexer was in a faulted state during dispose. Exception: {Exception}", connectionTask.Exception?.InnerException);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error disposing Redis connection multiplexer.");
                    }
                }
            }
            _disposed = true;
        }
    }
}
