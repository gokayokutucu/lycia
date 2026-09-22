// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
namespace Lycia.Common.SagaSteps;

/// <summary>
/// The lifecycle of a durable compensation propagation edge - the requirement that a compensated child
/// step's logical parent (via <c>ParentMessageId</c>) must also be compensated. This is a separate fact
/// from the child step's own <see cref="Lycia.Common.Enums.StepStatus.Compensated"/> status: the step
/// status records that the child's own compensation (business undo plus its framework state transition)
/// finished, while this status records whether that fact has actually been handed off to, and completed
/// by, the parent.
/// </summary>
public enum CompensationPropagationStatus
{
    /// <summary>Durably required and not currently owned by any worker or in-request attempt.</summary>
    Pending,

    /// <summary>Owned by a worker or the immediate in-request attempt for up to its lease/recovery window.</summary>
    Claimed,

    /// <summary>The parent's compensation handler has been invoked and the edge is resolved. Terminal.</summary>
    Completed,

    /// <summary>Every permitted attempt was exhausted without completing. Terminal; needs operator attention.</summary>
    Failed
}

/// <summary>
/// A durable record of one compensation propagation edge: "the parent of this compensated child step must
/// also be compensated." Identity is <see cref="SagaId"/> + <see cref="ChildMessageId"/>, so creating an
/// intent for the same child twice is a safe no-op rather than a duplicate edge. Everything needed to
/// actually perform the propagation (the child's step type/handler type, its serialized payload, and the
/// parent's own step record) is already durable in the SagaStore's step log and is looked up from there
/// when the edge is attempted - this record exists only to make "is propagation for this edge still
/// outstanding, and who owns attempting it right now" independently and durably answerable, decoupled from
/// the child step's own terminal status.
/// </summary>
public sealed class CompensationPropagationIntent
{
    /// <summary>The saga this propagation edge belongs to.</summary>
    public Guid SagaId { get; set; }

    /// <summary>The compensated child step's message id.</summary>
    public Guid ChildMessageId { get; set; }

    /// <summary>
    /// The child's <c>ParentMessageId</c> at the moment this intent was created, kept only for cheap
    /// operator visibility. The actual parent lookup at attempt time is always the child step's current
    /// recorded lineage, not this denormalized copy.
    /// </summary>
    public Guid ParentMessageId { get; set; }

    /// <summary>The propagation edge's current lifecycle state.</summary>
    public CompensationPropagationStatus Status { get; set; }

    /// <summary>How many propagation attempts have been started for this edge.</summary>
    public int AttemptCount { get; set; }

    /// <summary>The current claim owner (an in-request attempt or a <c>CompensationWorker</c> instance), or <c>null</c> when not claimed.</summary>
    public string? Owner { get; set; }

    /// <summary>When this edge was first durably required.</summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>When this record was last updated (created, claimed, completed, or failed).</summary>
    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>Failure details recorded when <see cref="Status"/> reaches <see cref="CompensationPropagationStatus.Failed"/>.</summary>
    public SagaStepFailureInfo? FailureInfo { get; set; }
}

/// <summary>The result of attempting to obtain ownership of a compensation propagation edge for an immediate attempt.</summary>
public enum CompensationPropagationClaimOutcome
{
    /// <summary>The caller now owns the edge and should attempt propagation immediately.</summary>
    Claimed,

    /// <summary>The edge was already completed; nothing to do. Idempotent no-op.</summary>
    AlreadyCompleted,

    /// <summary>Another owner (a concurrent attempt or a <c>CompensationWorker</c>) currently holds a live claim.</summary>
    ClaimedByAnother,

    /// <summary>Every permitted attempt has already been made; the edge is <see cref="CompensationPropagationStatus.Failed"/>.</summary>
    AttemptsExhausted
}

/// <summary>The outcome of <see cref="Lycia.Saga.Abstractions.ISagaStore.EnsureAndClaimCompensationPropagationAsync"/>.</summary>
public readonly struct CompensationPropagationClaim(CompensationPropagationClaimOutcome outcome, CompensationPropagationIntent? intent)
{
    /// <summary>The claim outcome.</summary>
    public CompensationPropagationClaimOutcome Outcome { get; } = outcome;

    /// <summary>The current intent record, present for every outcome.</summary>
    public CompensationPropagationIntent? Intent { get; } = intent;
}
