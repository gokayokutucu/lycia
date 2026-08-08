// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Extensions;

namespace Lycia.Persistence.InMemory;

/// <summary>
/// Fluent DSL entry points for the in-memory Inbox/Outbox on <see cref="LyciaPersistenceBuilder"/>.
/// For tests and local development only — state is not durable across process restarts.
/// </summary>
public static class InMemoryInboxOutboxDslExtensions
{
    /// <summary>Enables the in-memory Inbox (disabled by default).</summary>
    public static LyciaPersistenceBuilder WithInMemoryInbox(this LyciaPersistenceBuilder persistence)
    {
        if (persistence == null) throw new ArgumentNullException(nameof(persistence));
        return persistence.WithInbox<InMemoryInboxStore>();
    }

    /// <summary>Enables the in-memory Outbox (disabled by default).</summary>
    public static LyciaPersistenceBuilder WithInMemoryOutbox(this LyciaPersistenceBuilder persistence)
    {
        if (persistence == null) throw new ArgumentNullException(nameof(persistence));
        return persistence.WithOutbox<InMemoryOutboxStore>();
    }
}
