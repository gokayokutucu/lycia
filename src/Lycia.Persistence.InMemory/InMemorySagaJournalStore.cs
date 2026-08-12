// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
using System.Collections.Concurrent;
using Lycia.Saga.Abstractions.Persistence.Journal;

namespace Lycia.Persistence.InMemory;

/// <summary>
/// Deterministic in-memory <see cref="ISagaJournalStore"/> for reducer/rebuild/continuity tests. Not
/// durable — state is lost on process restart, and this is never a substitute for a real canonical
/// relational journal in production.
/// </summary>
public sealed class InMemorySagaJournalStore : ISagaJournalStore
{
    private readonly ConcurrentDictionary<Guid, List<SagaJournalEntry>> _entriesBySaga = new();
    private readonly ConcurrentDictionary<Guid, byte> _appliedTransitionIds = new();
    private readonly List<Guid> _sagaIdsInsertionOrder = [];
    private readonly object _lock = new();

    public Task AppendAsync(SagaJournalEntry entry, CancellationToken cancellationToken = default)
    {
        if (entry == null) throw new ArgumentNullException(nameof(entry));

        lock (_lock)
        {
            if (!_appliedTransitionIds.TryAdd(entry.TransitionId, 0))
                return Task.CompletedTask; // Idempotent no-op: this exact transition was already appended.

            var list = _entriesBySaga.GetOrAdd(entry.SagaId, _ => []);

            var expectedPrevious = list.Count == 0 ? 0 : list[list.Count - 1].TargetVersion;
            if (entry.PreviousVersion != expectedPrevious || entry.TargetVersion <= entry.PreviousVersion)
            {
                _appliedTransitionIds.TryRemove(entry.TransitionId, out _);
                throw new InvalidOperationException(
                    $"Journal continuity violation for saga {entry.SagaId}: expected a transition from version " +
                    $"{expectedPrevious}, but got one from {entry.PreviousVersion} to {entry.TargetVersion}.");
            }

            list.Add(entry);
            if (list.Count == 1) _sagaIdsInsertionOrder.Add(entry.SagaId);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SagaJournalEntry>> ReadAsync(Guid sagaId, long afterVersion, int maxCount,
        CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (!_entriesBySaga.TryGetValue(sagaId, out var list))
                return Task.FromResult<IReadOnlyList<SagaJournalEntry>>(Array.Empty<SagaJournalEntry>());

            var page = list.Where(e => e.TargetVersion > afterVersion)
                .OrderBy(e => e.SequenceNumber)
                .Take(maxCount)
                .ToList();
            return Task.FromResult<IReadOnlyList<SagaJournalEntry>>(page);
        }
    }

    public Task<long> GetLatestVersionAsync(Guid sagaId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_entriesBySaga.TryGetValue(sagaId, out var list) && list.Count > 0
                ? list[list.Count - 1].TargetVersion
                : 0L);
        }
    }

    public Task<IReadOnlyList<Guid>> EnumerateSagaIdsAsync(Guid? afterSagaId, int maxCount,
        CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var ordered = _sagaIdsInsertionOrder.AsEnumerable();
            if (afterSagaId.HasValue)
            {
                var index = _sagaIdsInsertionOrder.IndexOf(afterSagaId.Value);
                ordered = index >= 0 ? _sagaIdsInsertionOrder.Skip(index + 1) : ordered;
            }

            return Task.FromResult<IReadOnlyList<Guid>>(ordered.Take(maxCount).ToList());
        }
    }

    /// <summary>Test-only helper to inject a raw entry bypassing continuity validation, for corruption-detection fixtures.</summary>
    public void SeedRaw(SagaJournalEntry entry)
    {
        _appliedTransitionIds.TryAdd(entry.TransitionId, 0);
        var list = _entriesBySaga.GetOrAdd(entry.SagaId, _ => []);
        lock (_lock)
        {
            list.Add(entry);
            if (list.Count == 1) _sagaIdsInsertionOrder.Add(entry.SagaId);
        }
    }
}
