// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
using Lycia.Extensions;
using Lycia.Extensions.Configurations;
using Lycia.Saga.Abstractions.Persistence.Reconciliation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lycia.Persistence.Redis;

/// <summary>Registers Redis as the rebuildable operational projection side of Split Store.</summary>
public static class RedisOperationalSagaStoreDslExtensions
{
    /// <summary>
    /// Selects Redis only as an operational, rebuildable Saga projection. This does not reinterpret or
    /// replace the standalone <c>WithRedisSagaStore</c> behavior.
    /// </summary>
    public static LyciaPersistenceBuilder WithRedisOperationalSagaStore(
        this LyciaPersistenceBuilder persistence,
        Action<SagaStoreOptions>? configure = null)
    {
        if (persistence == null) throw new ArgumentNullException(nameof(persistence));
        if (configure != null) persistence.Services.Configure(configure);
        RedisSagaStoreRegistrationExtensions.RegisterRedisConnection(persistence.Services);
        persistence.Services.RemoveAll(typeof(IOperationalSagaProjectionStore));
        persistence.Services.AddScoped<IOperationalSagaProjectionStore, RedisOperationalSagaProjectionStore>();
        persistence.SelectSplitStoreOperationalProvider("Redis");
        return persistence;
    }
}
