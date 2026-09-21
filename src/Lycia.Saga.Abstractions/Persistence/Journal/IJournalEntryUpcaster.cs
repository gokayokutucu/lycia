// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
namespace Lycia.Saga.Abstractions.Persistence.Journal;

/// <summary>
/// Deterministic, side-effect-free upgrade of one journal entry from an older
/// <see cref="SagaJournalEntry.JournalSchemaVersion"/> to the next. Chained by a rebuild engine until
/// the entry reaches <see cref="SagaJournalSchema.CurrentVersion"/>. A missing upcaster for an entry's
/// schema version must fail the rebuild/verify clearly rather than guessing.
/// </summary>
public interface IJournalEntryUpcaster
{
    /// <summary>The schema version this upcaster reads.</summary>
    int FromSchemaVersion { get; }

    /// <summary>The schema version this upcaster produces (normally <see cref="FromSchemaVersion"/> + 1).</summary>
    int ToSchemaVersion { get; }

    /// <summary>Produces an equivalent entry at <see cref="ToSchemaVersion"/>. Must not mutate the persisted historical record.</summary>
    SagaJournalEntry Upcast(SagaJournalEntry entry);
}
