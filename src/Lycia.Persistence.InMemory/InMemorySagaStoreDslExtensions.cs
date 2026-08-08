// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Extensions;

namespace Lycia.Persistence.InMemory;

/// <summary>
/// Fluent DSL entry point for selecting the in-memory <c>ISagaStore</c> provider on
/// <see cref="LyciaPersistenceBuilder"/>. Mirrors the transport provider pattern
/// (e.g. <c>RabbitMqTransportDslExtensions.RabbitMq</c>).
/// </summary>
public static class InMemorySagaStoreDslExtensions
{
    /// <summary>Selects the in-memory <c>ISagaStore</c> as the active SagaStore provider.</summary>
    public static LyciaPersistenceBuilder WithInMemorySagaStore(this LyciaPersistenceBuilder persistence)
    {
        if (persistence == null) throw new ArgumentNullException(nameof(persistence));
        persistence.SelectProvider("InMemory");
        InMemorySagaStoreRegistrationExtensions.RegisterInMemorySagaStore(persistence.Services);
        return persistence;
    }
}
