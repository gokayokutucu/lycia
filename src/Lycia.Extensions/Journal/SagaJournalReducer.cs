// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
using Lycia.Saga.Abstractions.Persistence.Journal;

namespace Lycia.Extensions.Journal;

/// <summary>
/// Deterministic, side-effect-free reducer. Because each <see cref="SagaJournalEntry"/> already carries
/// the complete post-transition saga-data and step-log snapshot (not a delta), folding is a direct
/// projection from the entry rather than a merge — this is also what makes rebuild cheap: the final
/// state for a saga is fully determined by its single latest entry, while the ordered walk through
/// history remains necessary to prove continuity (no gaps, no backward transitions).
/// </summary>
public sealed class SagaJournalReducer : ISagaJournalReducer
{
    /// <inheritdoc />
    public SagaJournalState Reduce(SagaJournalState? previous, SagaJournalEntry entry)
    {
        if (entry == null) throw new ArgumentNullException(nameof(entry));

        return new SagaJournalState
        {
            SagaId = entry.SagaId,
            Version = entry.TargetVersion,
            SagaDataTypeName = entry.SagaDataTypeName,
            SagaDataPayload = entry.SagaDataPayload,
            StepsSnapshotPayload = entry.StepsSnapshotPayload,
            IsCompleted = entry.TransitionType == SagaJournalTransitionType.Completed,
            IsFailed = entry.TransitionType == SagaJournalTransitionType.Failed,
            LastTransitionType = entry.TransitionType,
            LastJournalEntryId = entry.JournalEntryId,
            LastTransitionId = entry.TransitionId
        };
    }
}
