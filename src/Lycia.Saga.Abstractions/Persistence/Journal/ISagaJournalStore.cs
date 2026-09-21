// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
namespace Lycia.Saga.Abstractions.Persistence.Journal;

/// <summary>
/// Relational canonical storage for the immutable saga transition journal. Implementations must not
/// expose provider connection/transaction details on this contract — a LocalAtomic append enlists in
/// the ambient <see cref="ILyciaPersistenceSessionAccessor"/> session internally, the same way
/// <c>IReconciliationStore</c> does.
/// </summary>
public interface ISagaJournalStore
{
    /// <summary>
    /// Appends one transition in the current canonical transaction. Idempotent on
    /// <see cref="SagaJournalEntry.TransitionId"/> — appending an already-recorded transition is a
    /// safe no-op, never a duplicate row.
    /// </summary>
    Task AppendAsync(SagaJournalEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads entries for one saga strictly ordered by <see cref="SagaJournalEntry.SequenceNumber"/>,
    /// starting after <paramref name="afterVersion"/> (0 to read from the beginning), bounded by
    /// <paramref name="maxCount"/> so callers never load unbounded history into memory.
    /// </summary>
    Task<IReadOnlyList<SagaJournalEntry>> ReadAsync(Guid sagaId, long afterVersion, int maxCount,
        CancellationToken cancellationToken = default);

    /// <summary>Gets the latest committed sequence number for a saga, or 0 when no entry exists.</summary>
    Task<long> GetLatestVersionAsync(Guid sagaId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enumerates distinct SagaIds that have at least one journal entry, in a stable order, for
    /// cursor-based bulk rebuild. Pass the last SagaId already processed as <paramref name="afterSagaId"/>
    /// to resume.
    /// </summary>
    Task<IReadOnlyList<Guid>> EnumerateSagaIdsAsync(Guid? afterSagaId, int maxCount,
        CancellationToken cancellationToken = default);
}
