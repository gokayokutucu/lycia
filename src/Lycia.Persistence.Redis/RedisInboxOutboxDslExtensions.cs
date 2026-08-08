// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Extensions;
using Lycia.Extensions.Configurations;
using Lycia.Outbox;
using Lycia.Saga.Abstractions.Inbox;
using Lycia.Saga.Abstractions.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Lycia.Persistence.Redis;

/// <summary>
/// Contributes the Redis Inbox/Outbox providers to <see cref="LyciaPersistenceBuilder"/>, mirroring
/// <see cref="RedisSagaStoreDslExtensions.WithRedisSagaStore"/>.
/// </summary>
public static class RedisInboxOutboxDslExtensions
{
    /// <summary>
    /// Selects Redis as the Inbox provider. Connection settings are read from
    /// <c>Lycia:Persistence:Inbox</c> (<see cref="InboxOptions"/>) unless overridden by <paramref name="configure"/>.
    /// </summary>
    public static LyciaPersistenceBuilder WithRedisInbox(
        this LyciaPersistenceBuilder persistence,
        Action<InboxOptions>? configure = null)
    {
        if (persistence == null) throw new ArgumentNullException(nameof(persistence));

        persistence.SelectInboxProvider("Redis");
        if (configure != null) persistence.Services.Configure(configure);

        EnsureRedisConnection<InboxOptions>(persistence.Services, "Lycia:Persistence:Inbox");

        persistence.Services.RemoveAll(typeof(IInboxStore));
        persistence.Services.AddScoped<IInboxStore>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<InboxOptions>>().Value;
            var redis = sp.GetRequiredService<IDatabase>();
            return new RedisInboxStore(redis, opts);
        });

        return persistence;
    }

    /// <summary>
    /// Selects Redis as the Outbox provider. Connection settings are read from
    /// <c>Lycia:Persistence:Outbox</c> (<see cref="OutboxOptions"/>) unless overridden by <paramref name="configure"/>.
    /// </summary>
    public static LyciaPersistenceBuilder WithRedisOutbox(
        this LyciaPersistenceBuilder persistence,
        Action<OutboxOptions>? configure = null)
    {
        if (persistence == null) throw new ArgumentNullException(nameof(persistence));

        persistence.SelectOutboxProvider("Redis");
        if (configure != null) persistence.Services.Configure(configure);

        EnsureRedisConnection<OutboxOptions>(persistence.Services, "Lycia:Persistence:Outbox");

        persistence.Services.RemoveAll(typeof(IOutboxStore));
        persistence.Services.AddScoped<IOutboxStore>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<OutboxOptions>>().Value;
            var redis = sp.GetRequiredService<IDatabase>();
            return new RedisOutboxStore(redis, opts);
        });
        persistence.Services.TryAddScoped<IOutboxDispatcher, OutboxDispatcher>();

        return persistence;
    }

    // Bypasses the generic WithInbox<T>()/WithOutbox<T>() escape hatches (which would re-derive provider
    // selection from the generic type name) since SelectInboxProvider/SelectOutboxProvider is already
    // called explicitly above with the canonical "Redis" name, the same pattern WithRedisSagaStore uses
    // for SelectProvider("Redis") rather than routing through a generic wrapper.
    private static void EnsureRedisConnection<TOptions>(IServiceCollection services, string sectionName)
        where TOptions : class
    {
        services.TryAddSingleton<IConnectionMultiplexer>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<TOptions>>().Value;
            var connectionString = GetConnectionString(opts);
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException(
                    $"{sectionName}:ConnectionString is required for the Redis Inbox/Outbox provider. " +
                    "Set it via configuration or the With...(o => o.ConnectionString = ...) configure callback.");

            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Lycia.Persistence.Redis");
            try
            {
                return ConnectionMultiplexer.Connect(connectionString);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Lycia failed to connect to Redis while initializing the Inbox/Outbox store. Check {SectionName} settings.",
                    sectionName);
                throw new InvalidOperationException(
                    "Lycia was unable to initialize the Redis connection for the Inbox/Outbox store. See inner exception for details.",
                    ex);
            }
        });

        services.TryAddScoped<IDatabase>(sp =>
            sp.GetRequiredService<IConnectionMultiplexer>().GetDatabase());
    }

    private static string? GetConnectionString(object options) => options switch
    {
        InboxOptions inbox => inbox.ConnectionString,
        OutboxOptions outbox => outbox.ConnectionString,
        _ => null
    };
}
