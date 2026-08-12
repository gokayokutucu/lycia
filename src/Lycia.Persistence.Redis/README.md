# Lycia.Persistence.Redis

Redis-backed `ISagaStore` provider for the [Lycia](https://github.com/gokayokutucu/lycia) saga framework.

`WithRedisSagaStore` retains standalone canonical Redis behavior. Split Store uses the distinct
`WithRedisOperationalSagaStore` API, whose data is rebuildable and only receives versioned canonical
relational state through durable reconciliation.

## Usage

```csharp
services.AddLycia(configuration, lycia =>
{
    lycia
        .UsePersistence()
            .WithRedisSagaStore();
});
```

Provides:
- `RedisSagaStore`: step-log and saga-data persistence backed by Redis, using atomic Lua-script CAS operations.
- Optimistic concurrency via `IVersionedSagaStore` (`SaveSagaDataAsync(sagaId, data, expectedVersion)` / `LoadSagaDataWithVersionAsync`).
- Automatic Redis connection setup from `SagaStoreOptions.ConnectionString` when no `IDatabase` is already registered.
