using System.Reflection;
using Lycia.Extensions.Eventing;
using Lycia.Extensions.Stores;
using Lycia.Infrastructure.Listener;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Common;
using Lycia.Saga.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Lycia.Extensions;

public static class LyciaRegistrationExtension
{
    public static ILyciaServiceCollection AddLycia(this IServiceCollection services, IConfiguration? configuration = null,
        Type? sagaType = null)
    {
        if(configuration is null ) return new LyciaServiceCollection(services, null);
        
        var eventBusProvider = configuration["Lycia:EventBus:Provider"] ?? "RabbitMQ";
        var eventStoreProvider = configuration["Lycia:EventStore:Provider"] ?? "Redis";

        // Add Redis connection (if Provider is Redis)
        if (eventStoreProvider == "Redis")
        {
            var conn = configuration["Lycia:EventStore:ConnectionString"]
                       ?? throw new InvalidOperationException("Lycia:EventStore:ConnectionString is not configured.");
            services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(conn));
            services.AddScoped<IDatabase>(provider =>
                provider.GetRequiredService<IConnectionMultiplexer>().GetDatabase());
        }

        var queueTypeMap =
            SagaHandlerRegistrationExtensions.DiscoverQueueTypeMap(sagaType?.Assembly ?? Assembly.GetCallingAssembly());

        // Configure and add RabbitMqEventBus (if Provider is RabbitMQ)
        if (eventBusProvider == "RabbitMQ")
        {
            services.AddSingleton<IEventBus>(provider =>
            {
                var conn = configuration["Lycia:EventBus:ConnectionString"]
                           ?? throw new InvalidOperationException("Lycia:EventBus:ConnectionString is not configured.");
                var logger = provider.GetRequiredService<ILogger<RabbitMqEventBus>>();
                return RabbitMqEventBus.CreateAsync(conn, logger, queueTypeMap).GetAwaiter().GetResult();
            });

            services.AddHostedService<RabbitMqListener>();
        }

        // Configure and add RedisSagaStore
        if (eventStoreProvider == "Redis")
        {
            services.AddScoped<ISagaStore, RedisSagaStore>(provider =>
            {
                var redisDb = provider.GetRequiredService<IDatabase>();
                var eventBus = provider.GetRequiredService<IEventBus>();
                var sagaIdGen = provider.GetRequiredService<ISagaIdGenerator>();
                return new RedisSagaStore(redisDb, eventBus, sagaIdGen);
            });
        }

        return new LyciaServiceCollection(services, configuration);
    }
}