// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
namespace Lycia.Saga.Abstractions.Persistence.Journal;

/// <summary>
/// Deterministic saga state reconstructed by <see cref="ISagaJournalReducer"/>. Contains exactly what
/// current Lycia SagaStore/operational-projection semantics need — not a general-purpose event-sourced
/// aggregate.
/// </summary>
public sealed class SagaJournalState
{
    public Guid SagaId { get; set; }
    public long Version { get; set; }
    public string SagaDataTypeName { get; set; } = string.Empty;
    public string SagaDataPayload { get; set; } = string.Empty;
    public string? StepsSnapshotPayload { get; set; }
    public bool IsCompleted { get; set; }
    public bool IsFailed { get; set; }
    public SagaJournalTransitionType LastTransitionType { get; set; }
    public Guid LastJournalEntryId { get; set; }
    public Guid LastTransitionId { get; set; }
}
