// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
namespace Lycia.Saga.Abstractions.Persistence.Journal;

/// <summary>Classifies why a per-saga rebuild/verify could not proceed.</summary>
public enum SagaJournalFailureKind
{
    None,
    /// <summary>A sequence number is missing from otherwise ordered history.</summary>
    JournalGap,
    /// <summary>An entry's schema version has no registered upcaster.</summary>
    SchemaUnsupported,
    /// <summary>An entry violates continuity (backward version, duplicate sequence, wrong SagaId, malformed payload).</summary>
    CorruptEntry
}

/// <summary>Result of rebuilding one saga's operational projection from its canonical journal.</summary>
public sealed class SagaRebuildOutcome
{
    public Guid SagaId { get; set; }
    public bool Succeeded { get; set; }
    public long? RebuiltVersion { get; set; }
    public SagaJournalFailureKind FailureKind { get; set; } = SagaJournalFailureKind.None;
    public string? FailureReason { get; set; }

    public static SagaRebuildOutcome Success(Guid sagaId, long version) =>
        new() { SagaId = sagaId, Succeeded = true, RebuiltVersion = version };

    public static SagaRebuildOutcome Failure(Guid sagaId, SagaJournalFailureKind kind, string reason) =>
        new() { SagaId = sagaId, Succeeded = false, FailureKind = kind, FailureReason = reason };
}

/// <summary>Non-mutating comparison outcome between journal-derived state and the live system.</summary>
public enum SagaProjectionVerificationStatus
{
    /// <summary>Canonical, journal-derived, and operational projection versions all agree.</summary>
    Healthy,
    /// <summary>The saga has journal history but no operational projection exists.</summary>
    MissingProjection,
    /// <summary>The operational projection version does not match the journal-derived version.</summary>
    VersionMismatch,
    /// <summary>The canonical SagaStore's current version does not match the journal-derived version.</summary>
    StateMismatch,
    JournalGap,
    SchemaUnsupported,
    CorruptEntry
}

public sealed class SagaVerificationOutcome
{
    public Guid SagaId { get; set; }
    public SagaProjectionVerificationStatus Status { get; set; }
    public long? JournalVersion { get; set; }
    public long? OperationalProjectionVersion { get; set; }
    public long? CanonicalVersion { get; set; }
    public string? Detail { get; set; }
}

/// <summary>Bounded-batch progress for a full rebuild/verify pass, suitable for <see cref="IProgress{T}"/>.</summary>
public sealed class SagaRebuildProgress
{
    public int Processed { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public Guid? LastSagaId { get; set; }
}

/// <summary>Options bounding a full rebuild/verify pass.</summary>
public sealed class SagaRebuildBatchOptions
{
    /// <summary>How many SagaIds to process per page.</summary>
    public int PageSize { get; set; } = 50;

    /// <summary>How many journal entries to read per store round trip while replaying one saga.</summary>
    public int JournalReadBatchSize { get; set; } = 200;

    /// <summary>Resume a previous bulk pass by skipping SagaIds up to and including this one.</summary>
    public Guid? ResumeAfterSagaId { get; set; }
}

/// <summary>Summary of a bounded full rebuild/verify pass. <see cref="ResumeCursor"/> can seed a later resumed pass.</summary>
public sealed class SagaBulkOperationSummary
{
    public int Processed { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public IReadOnlyList<Guid> FailedSagaIds { get; set; } = Array.Empty<Guid>();
    public Guid? ResumeCursor { get; set; }
    public bool Cancelled { get; set; }
}

/// <summary>
/// One reusable engine for both automatic recovery and manual/operator-triggered rebuild — the same
/// logic future Lycia Doctor commands (<c>rebuild redis</c>, <c>rebuild saga</c>, <c>verify projections</c>)
/// would call. Never executes business/compensation handlers, never publishes, never writes Inbox/Outbox,
/// never generates new MessageIds.
/// </summary>
public interface ISagaRebuildService
{
    /// <summary>Rebuilds one saga's operational projection from its canonical journal history.</summary>
    Task<SagaRebuildOutcome> RebuildSagaAsync(Guid sagaId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rebuilds every saga with journal history, in bounded pages, isolating per-saga failures so one
    /// corrupt saga does not abort the rest.
    /// </summary>
    Task<SagaBulkOperationSummary> RebuildAllAsync(SagaRebuildBatchOptions? options = null,
        IProgress<SagaRebuildProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Compares journal-derived state against the canonical and operational projection without modifying anything.</summary>
    Task<SagaVerificationOutcome> VerifySagaAsync(Guid sagaId, CancellationToken cancellationToken = default);

    /// <summary>Verifies every saga with journal history, in bounded pages, without modifying anything.</summary>
    Task<SagaBulkOperationSummary> VerifyAllAsync(SagaRebuildBatchOptions? options = null,
        IProgress<SagaRebuildProgress>? progress = null, CancellationToken cancellationToken = default);
}
