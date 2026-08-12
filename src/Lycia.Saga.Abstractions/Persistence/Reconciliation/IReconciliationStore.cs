// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
namespace Lycia.Saga.Abstractions.Persistence.Reconciliation;

/// <summary>Relational canonical storage for durable Split Store projection intents.</summary>
public interface IReconciliationStore
{
    /// <summary>Adds an intent in the current canonical transaction.</summary>
    Task AddAsync(SagaProjectionIntent intent, CancellationToken cancellationToken = default);
    /// <summary>Claims due work while recovering bounded stale claims.</summary>
    Task<IReadOnlyList<SagaProjectionIntent>> ClaimAsync(string workerId, int batchSize, int maxAttempts,
        TimeSpan claimTimeout, CancellationToken cancellationToken = default);
    /// <summary>Marks an intent applied or superseded.</summary>
    Task MarkCompletedAsync(Guid transitionId, ReconciliationStatus status,
        CancellationToken cancellationToken = default);
    /// <summary>Schedules a transient failure for another bounded attempt.</summary>
    Task MarkRetryAsync(Guid transitionId, DateTime nextAttemptAtUtc, string failureCode,
        CancellationToken cancellationToken = default);
    /// <summary>Marks malformed or exhausted work terminally failed.</summary>
    Task MarkFailedAsync(Guid transitionId, string failureCode, CancellationToken cancellationToken = default);
    /// <summary>Queues the latest canonical state again for operational projection restoration.</summary>
    Task<bool> QueueLatestAsync(Guid sagaId, CancellationToken cancellationToken = default);
}

/// <summary>Applies canonical state to a rebuildable operational saga projection.</summary>
public interface IOperationalSagaProjectionStore
{
    /// <summary>Applies the target version idempotently without overwriting newer state.</summary>
    Task<ProjectionApplyOutcome> ApplyAsync(SagaProjectionIntent intent,
        CancellationToken cancellationToken = default);
    /// <summary>Gets the currently materialized version, or zero when absent.</summary>
    Task<long> GetVersionAsync(Guid sagaId, CancellationToken cancellationToken = default);
    /// <summary>Deletes only one saga's rebuildable operational projection.</summary>
    Task DeleteAsync(Guid sagaId, CancellationToken cancellationToken = default);
}

/// <summary>Coordinates one reconciliation pass and projection restoration requests.</summary>
public interface ISagaProjectionReconciler
{
    /// <summary>Claims and applies one bounded batch.</summary>
    Task<ReconciliationRunResult> RunOnceAsync(CancellationToken cancellationToken = default);
    /// <summary>Queues the latest canonical state for restoration without executing a handler.</summary>
    Task<bool> RestoreLatestAsync(Guid sagaId, CancellationToken cancellationToken = default);
}

/// <summary>Summary of a bounded reconciliation pass.</summary>
public sealed class ReconciliationRunResult
{
    /// <summary>Number claimed.</summary>
    public int Claimed { get; set; }
    /// <summary>Number applied or already applied.</summary>
    public int Applied { get; set; }
    /// <summary>Number classified as superseded.</summary>
    public int Superseded { get; set; }
    /// <summary>Number scheduled for retry.</summary>
    public int Retried { get; set; }
    /// <summary>Number terminally failed.</summary>
    public int Failed { get; set; }
}
