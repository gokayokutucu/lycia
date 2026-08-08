// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Extensions;
using Lycia.Extensions.Configurations;
using Microsoft.Extensions.DependencyInjection;

namespace Lycia.Persistence.Redis;

/// <summary>
/// Contributes the Redis provider to <see cref="LyciaPersistenceBuilder"/>. Lycia.Extensions defines the
/// builder and its provider-selection guard; this package only adds a provider method to it, so
/// Lycia.Extensions never depends on Lycia.Persistence.Redis.
/// </summary>
public static class RedisSagaStoreDslExtensions
{
    /// <summary>
    /// Selects Redis as the SagaStore provider. Connection settings are read from <c>Lycia:EventStore</c>
    /// (<see cref="SagaStoreOptions"/>) unless overridden by <paramref name="configure"/>.
    /// </summary>
    public static LyciaPersistenceBuilder WithRedisSagaStore(
        this LyciaPersistenceBuilder persistence,
        Action<SagaStoreOptions>? configure = null)
    {
        if (persistence == null) throw new ArgumentNullException(nameof(persistence));

        persistence.SelectProvider("Redis");
        if (configure != null) persistence.Services.Configure(configure);
        RedisSagaStoreRegistrationExtensions.RegisterRedisSagaStore(persistence.Services);
        return persistence;
    }
}
