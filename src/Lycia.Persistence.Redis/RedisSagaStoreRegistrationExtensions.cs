// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Extensions.Configurations;
using Lycia.Saga.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Lycia.Persistence.Redis;

/// <summary>
/// Registers the Redis-backed <see cref="ISagaStore"/> for <see cref="RedisSagaStoreDslExtensions.WithRedisSagaStore"/>.
/// </summary>
internal static class RedisSagaStoreRegistrationExtensions
{
    /// <summary>
    /// Registers <see cref="RedisSagaStore"/> as the <see cref="ISagaStore"/> implementation, and ensures a
    /// Redis connection (<see cref="IConnectionMultiplexer"/>/<see cref="IDatabase"/>) is available, establishing
    /// one from <see cref="SagaStoreOptions.ConnectionString"/> when the host app hasn't already registered one.
    /// </summary>
    public static void RegisterRedisSagaStore(IServiceCollection services)
    {
        services.TryAddSingleton<IConnectionMultiplexer>(sp =>
        {
            var storeOpts = sp.GetRequiredService<IOptions<SagaStoreOptions>>().Value;
            var connectionString = storeOpts.ConnectionString;
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException(
                    "Lycia:EventStore:ConnectionString is required for the Redis SagaStore provider. " +
                    "Set it via configuration or WithRedisSagaStore(o => o.ConnectionString = ...).");

            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Lycia.Persistence.Redis");
            try
            {
                return ConnectionMultiplexer.Connect(connectionString);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Lycia failed to connect to Redis while initializing the saga store. Check Lycia:EventStore settings.");
                throw new InvalidOperationException(
                    "Lycia was unable to initialize the Redis connection for the saga store. See inner exception for details.",
                    ex);
            }
        });

        services.TryAddScoped<IDatabase>(sp =>
            sp.GetRequiredService<IConnectionMultiplexer>().GetDatabase());

        services.RemoveAll(typeof(ISagaStore));
        services.AddScoped<ISagaStore>(sp =>
        {
            var storeOpts = sp.GetRequiredService<IOptions<SagaStoreOptions>>().Value;
            var eventBus = sp.GetRequiredService<IEventBus>();
            var idGen = sp.GetRequiredService<ISagaIdGenerator>();
            var compCoord = sp.GetRequiredService<ISagaCompensationCoordinator>();
            var redis = sp.GetRequiredService<IDatabase>();
            return new RedisSagaStore(redis, eventBus, idGen, compCoord, storeOpts);
        });
    }
}
