// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
namespace Lycia.Saga.Abstractions.Persistence.Journal;

/// <summary>
/// One immutable canonical saga transition. Once appended, the identity and payload fields required
/// for deterministic replay must never change. Ordering authority is <see cref="SagaId"/> +
/// <see cref="SequenceNumber"/> — never <see cref="CreatedAtUtc"/>, which is diagnostic metadata only.
/// <see cref="SequenceNumber"/> and <see cref="TargetVersion"/> are deliberately the same value: Lycia
/// already has one authoritative per-saga monotonic counter (<c>SagaData.Version</c>), and this journal
/// reuses it rather than introducing a second, unrelated ordering axis.
/// </summary>
public sealed class SagaJournalEntry
{
    /// <summary>Unique identity of this journal record.</summary>
    public Guid JournalEntryId { get; set; }

    /// <summary>
    /// Deterministic idempotency identity for this transition, derived from (SagaId, TargetVersion).
    /// Appending the same transition twice (e.g. after a redelivered message that the Inbox did not
    /// suppress) must be a safe, recognized no-op rather than a duplicate row.
    /// </summary>
    public Guid TransitionId { get; set; }

    public Guid SagaId { get; set; }

    /// <summary>Ordering authority. Always equal to <see cref="TargetVersion"/>.</summary>
    public long SequenceNumber { get; set; }

    /// <summary>The saga version required for this transition to apply (0 for the first transition).</summary>
    public long PreviousVersion { get; set; }

    /// <summary>The saga version this transition establishes.</summary>
    public long TargetVersion { get; set; }

    /// <summary>The message that caused this transition, when a handler-dispatch context was available.</summary>
    public Guid? MessageId { get; set; }
    public Guid? RequestId { get; set; }
    public Guid? CorrelationId { get; set; }
    public Guid? CausationId { get; set; }
    public Guid? ParentMessageId { get; set; }
    public string? ApplicationId { get; set; }
    public string? HandlerType { get; set; }
    public string? MessageType { get; set; }

    /// <summary>Schema version of the message contract referenced by <see cref="MessageType"/>, when known.</summary>
    public int MessageSchemaVersion { get; set; } = 1;

    /// <summary>Schema version of this journal entry's own payload shape, for upcasting.</summary>
    public int JournalSchemaVersion { get; set; } = SagaJournalSchema.CurrentVersion;

    public SagaJournalTransitionType TransitionType { get; set; }

    /// <summary>Simplified qualified type name of the saga data captured in <see cref="SagaDataPayload"/>.</summary>
    public string SagaDataTypeName { get; set; } = string.Empty;

    /// <summary>Full post-transition saga data, serialized. Self-sufficient: the reducer does not need prior entries to interpret it.</summary>
    public string SagaDataPayload { get; set; } = string.Empty;

    /// <summary>
    /// Full post-transition snapshot of this saga's step log (all <c>SagaStepMetadata</c> known at the
    /// time of this transition), serialized. Captures step transition, compensation, and cancellation
    /// state without requiring the reducer to fold per-step deltas.
    /// </summary>
    public string? StepsSnapshotPayload { get; set; }

    /// <summary>Diagnostic metadata only — never used to order or gap-check replay.</summary>
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>Current journal payload schema version understood by <see cref="ISagaJournalReducer"/> without upcasting.</summary>
public static class SagaJournalSchema
{
    public const int CurrentVersion = 1;
}
