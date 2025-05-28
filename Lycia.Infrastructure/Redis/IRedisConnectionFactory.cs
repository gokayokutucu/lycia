using StackExchange.Redis;
using System;

namespace Lycia.Infrastructure.Redis
{
    /// <summary>
    /// Defines a contract for a factory that provides connections to a Redis server.
    /// </summary>
    public interface IRedisConnectionFactory : IDisposable
    {
        /// <summary>
        /// Gets a Redis database instance.
        /// </summary>
        /// <param name="db">The specific database number to use. Defaults to the Redis default database.</param>
        /// <returns>An <see cref="IDatabase"/> instance.</returns>
        IDatabase GetDatabase(int db = -1);

        /// <summary>
        /// Gets the underlying connection multiplexer.
        /// Useful for advanced scenarios like accessing subscribers.
        /// </summary>
        IConnectionMultiplexer GetConnectionMultiplexer();
    }
}
