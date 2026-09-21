# Lycia.Persistence.Redis

Redis-backed SagaStore, Inbox and Outbox providers, and the Split Store operational projection, for the
[Lycia](https://github.com/gokayokutucu/lycia) saga framework.

`WithRedisSagaStore` retains standalone canonical Redis behavior. Split Store uses the distinct
`WithRedisOperationalSagaStore` API, whose data is rebuildable and only receives versioned canonical
relational state through durable reconciliation.

## Usage

```csharp
services.AddLycia(configuration, lycia =>
{
    lycia
        .UsePersistence()
            .WithRedisSagaStore(options => options.ConnectionString = redis)
            .WithRedisInbox(options => options.ConnectionString = redis)    // optional
            .WithRedisOutbox(options => options.ConnectionString = redis);  // optional
});
```

Provides:
- `RedisSagaStore`: step-log and saga-data persistence using atomic Lua-script compare-and-set, with
  optimistic concurrency via `IVersionedSagaStore`.
- `RedisInboxStore`: `(MessageId, HandlerType)` claims with atomic, timeout-gated takeover of stale
  `Processing`/`Failed` claims (`InboxOptions.ClaimRecoveryTimeout`, default 5 minutes).
- `RedisOutboxStore`: idempotent capture and atomic batch claiming through Lua scripts.
- `WithRedisOperationalSagaStore`: the version-fenced Split Store operational projection.

The Inbox and Outbox scripts touch several keys without hash tags, so they target standalone
(non-clustered) Redis. Redis stores never share a relational transaction; with Redis the persistence
boundary is always `Independent`.
