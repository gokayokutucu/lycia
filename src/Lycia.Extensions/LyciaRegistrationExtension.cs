using System.Reflection;
using Lycia.Extensions.Eventing;
using Lycia.Extensions.Stores;
using Lycia.Infrastructure.Compensating;
using Lycia.Infrastructure.Dispatching;
using Lycia.Infrastructure.Eventing;
using Lycia.Infrastructure.Listener;
using Lycia.Infrastructure.Stores;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Common;
using Lycia.Saga.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

        // Production default registration for ISagaIdGenerator
        services.TryAddScoped<ISagaIdGenerator, DefaultSagaIdGenerator>();
        // Production default registration for ISagaDispatcher
        services.TryAddScoped<ISagaDispatcher, SagaDispatcher>();
        // Production default registration for ISagaCompensationCoordinator
        services.TryAddScoped<ISagaCompensationCoordinator, SagaCompensationCoordinator>();

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
                var sagaCompensationCoordinator = provider.GetRequiredService<ISagaCompensationCoordinator>();
                var config = provider.GetService<IConfiguration>();
                int ttlSeconds = 3600; // Default: 1 hour
                if (config != null)
                {
                    var ttlConfig = config["Lycia:EventStore:StepLogTTL"];
                    if (int.TryParse(ttlConfig, out var parsedTtl) && parsedTtl > 0)
                        ttlSeconds = parsedTtl;
                }
                return new RedisSagaStore(redisDb, eventBus, sagaIdGen, sagaCompensationCoordinator, TimeSpan.FromSeconds(ttlSeconds));
            });
        }

        return new LyciaServiceCollection(services, configuration);
    }
    
    public static ILyciaServiceCollection AddLyciaInMemory(this IServiceCollection services)
    {
        services.TryAddScoped<ISagaIdGenerator, DefaultSagaIdGenerator>();
        services.TryAddScoped<ISagaDispatcher, SagaDispatcher>();
        services.TryAddScoped<ISagaCompensationCoordinator, SagaCompensationCoordinator>();
        services.TryAddScoped<ISagaStore, InMemorySagaStore>();
        services.TryAddScoped<IEventBus, InMemoryEventBus>();
        return new LyciaServiceCollection(services, null);
    }
}