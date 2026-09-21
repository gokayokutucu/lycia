// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
using System.Collections.Concurrent;
using Lycia.Saga.Abstractions.Persistence.Reconciliation;

namespace Lycia.Tests.Journal;

/// <summary>
/// Minimal in-memory <see cref="IOperationalSagaProjectionStore"/> test double replicating the documented
/// CAS/version-fencing contract (a stale target never overwrites a newer installed version), so
/// SagaRebuildService tests do not need a real Redis container to prove rebuild logic.
/// </summary>
public sealed class FakeOperationalSagaProjectionStore : IOperationalSagaProjectionStore
{
    private sealed class Entry
    {
        public long Version;
        public string Payload = string.Empty;
    }

    private readonly ConcurrentDictionary<Guid, Entry> _projections = new();

    public Task<ProjectionApplyOutcome> ApplyAsync(SagaProjectionIntent intent, CancellationToken cancellationToken = default)
    {
        var entry = _projections.GetOrAdd(intent.SagaId, _ => new Entry());
        lock (entry)
        {
            if (entry.Version > intent.TargetVersion) return Task.FromResult(ProjectionApplyOutcome.Superseded);
            if (entry.Version == intent.TargetVersion) return Task.FromResult(ProjectionApplyOutcome.AlreadyApplied);

            entry.Version = intent.TargetVersion;
            entry.Payload = intent.Payload;
            return Task.FromResult(ProjectionApplyOutcome.Applied);
        }
    }

    public Task<long> GetVersionAsync(Guid sagaId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_projections.TryGetValue(sagaId, out var entry) ? entry.Version : 0L);

    public Task DeleteAsync(Guid sagaId, CancellationToken cancellationToken = default)
    {
        _projections.TryRemove(sagaId, out _);
        return Task.CompletedTask;
    }
}
