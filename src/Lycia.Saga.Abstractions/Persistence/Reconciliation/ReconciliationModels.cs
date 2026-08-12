// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
namespace Lycia.Saga.Abstractions.Persistence.Reconciliation;

/// <summary>The durable lifecycle of a Split Store projection intent.</summary>
public enum ReconciliationStatus
{
    /// <summary>Ready for a worker to claim.</summary>
    Pending,
    /// <summary>Owned by a worker for a bounded lease.</summary>
    Claimed,
    /// <summary>Applied to the operational projection.</summary>
    Applied,
    /// <summary>Waiting for a bounded retry.</summary>
    RetryPending,
    /// <summary>Terminal because the payload cannot be applied safely.</summary>
    Failed,
    /// <summary>A newer projection already makes this intent unnecessary.</summary>
    Superseded
}

/// <summary>Classifies the result of applying one canonical state to an operational projection.</summary>
public enum ProjectionApplyOutcome
{
    /// <summary>The requested state was installed.</summary>
    Applied,
    /// <summary>The exact target version was already installed.</summary>
    AlreadyApplied,
    /// <summary>A newer version is already installed, so the intent is stale.</summary>
    Superseded,
    /// <summary>The operational value conflicts with the canonical version contract.</summary>
    VersionConflict
}

/// <summary>
/// A durable Phase 5 intent containing the resulting canonical saga state. It restores the current
/// operational projection and is not the Phase 6 immutable historical journal.
/// </summary>
public sealed class SagaProjectionIntent
{
    /// <summary>Stable idempotency identity for this projection transition.</summary>
    public Guid TransitionId { get; set; }
    /// <summary>The saga whose operational projection is affected.</summary>
    public Guid SagaId { get; set; }
    /// <summary>The triggering message identity when a handler context is available.</summary>
    public Guid? MessageId { get; set; }
    /// <summary>The deterministic predecessor version.</summary>
    public long ExpectedVersion { get; set; }
    /// <summary>The canonical version represented by <see cref="Payload"/>.</summary>
    public long TargetVersion { get; set; }
    /// <summary>The qualified saga-data type.</summary>
    public string SagaDataType { get; set; } = string.Empty;
    /// <summary>The serialized resulting canonical state.</summary>
    public string Payload { get; set; } = string.Empty;
    /// <summary>The durable lifecycle state.</summary>
    public ReconciliationStatus Status { get; set; }
    /// <summary>Number of worker attempts.</summary>
    public int AttemptCount { get; set; }
    /// <summary>Creation time for diagnostics, never ordering authority.</summary>
    public DateTime CreatedAtUtc { get; set; }
    /// <summary>Earliest time a retry may be claimed.</summary>
    public DateTime? NextAttemptAtUtc { get; set; }
}

/// <summary>Options for the bounded Split Store reconciliation worker.</summary>
public sealed class ReconciliationWorkerOptions
{
    /// <summary>Whether the hosted worker runs.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Maximum intents claimed per pass.</summary>
    public int BatchSize { get; set; } = 32;
    /// <summary>Maximum attempts before terminal failure.</summary>
    public int MaxAttempts { get; set; } = 10;
    /// <summary>Idle polling interval.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);
    /// <summary>Initial transient-failure backoff.</summary>
    public TimeSpan RetryBackoff { get; set; } = TimeSpan.FromSeconds(1);
    /// <summary>Maximum transient-failure backoff.</summary>
    public TimeSpan MaxRetryBackoff { get; set; } = TimeSpan.FromMinutes(1);
    /// <summary>Maximum random retry jitter.</summary>
    public TimeSpan MaxJitter { get; set; } = TimeSpan.FromMilliseconds(250);
    /// <summary>Age after which an abandoned claim can be recovered.</summary>
    public TimeSpan ClaimTimeout { get; set; } = TimeSpan.FromMinutes(2);
}
