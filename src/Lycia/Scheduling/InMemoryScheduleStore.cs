// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0

using Lycia.Saga.Abstractions.Scheduling;

namespace Lycia.Scheduling;

/// <summary>Atomic in-memory schedule store for deterministic tests and single-process development.</summary>
public sealed class InMemoryScheduleStore : IScheduleStore
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, ScheduleRecord> _records = new();

    /// <inheritdoc />
    public Task<ScheduleCreationResult> CreateAsync(ScheduleRecord record,
        CancellationToken cancellationToken = default)
    {
        if (record == null) throw new ArgumentNullException(nameof(record));
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_records.TryGetValue(record.ScheduleId, out var existing))
            {
                EnsureSameRequest(existing, record);
                return Task.FromResult(new ScheduleCreationResult { ScheduleId = existing.ScheduleId, Created = false });
            }

            _records.Add(record.ScheduleId, Clone(record));
            return Task.FromResult(new ScheduleCreationResult { ScheduleId = record.ScheduleId, Created = true });
        }
    }

    /// <inheritdoc />
    public Task<ScheduleRecord?> GetAsync(Guid scheduleId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return Task.FromResult(_records.TryGetValue(scheduleId, out var record) ? Clone(record) : null);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ScheduleClaim>> ClaimDueAsync(DateTimeOffset nowUtc, int maximumCount,
        string leaseOwner, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
    {
        if (maximumCount <= 0) throw new ArgumentOutOfRangeException(nameof(maximumCount));
        if (string.IsNullOrWhiteSpace(leaseOwner)) throw new ArgumentException("Lease owner is required.", nameof(leaseOwner));
        if (leaseDuration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedNow = nowUtc.ToUniversalTime();
        var claims = new List<ScheduleClaim>(maximumCount);
        lock (_gate)
        {
            var due = _records.Values
                .Where(record => IsClaimable(record, normalizedNow))
                .OrderBy(record => record.NextAttemptAtUtc ?? record.DueAtUtc)
                .ThenBy(record => record.ScheduleId)
                .Take(maximumCount)
                .ToArray();
            foreach (var record in due)
            {
                record.Status = ScheduleStatus.Claimed;
                record.LeaseOwner = leaseOwner;
                record.LeaseUntilUtc = normalizedNow.Add(leaseDuration);
                record.FencingToken++;
                claims.Add(new ScheduleClaim
                {
                    Record = Clone(record),
                    LeaseOwner = leaseOwner,
                    FencingToken = record.FencingToken
                });
            }
        }
        return Task.FromResult<IReadOnlyList<ScheduleClaim>>(claims);
    }

    /// <inheritdoc />
    public Task<bool> RenewLeaseAsync(Guid scheduleId, string leaseOwner, long fencingToken,
        DateTimeOffset leaseUntilUtc, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!TryGetOwned(scheduleId, leaseOwner, fencingToken, out var record)) return Task.FromResult(false);
            record.LeaseUntilUtc = leaseUntilUtc.ToUniversalTime();
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<bool> MarkDispatchingAsync(Guid scheduleId, string leaseOwner, long fencingToken,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!TryGetOwned(scheduleId, leaseOwner, fencingToken, out var record) ||
                record.Status != ScheduleStatus.Claimed) return Task.FromResult(false);
            record.Status = ScheduleStatus.Dispatching;
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<bool> CompleteAsync(Guid scheduleId, string leaseOwner, long fencingToken,
        DateTimeOffset completedAtUtc, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!TryGetOwned(scheduleId, leaseOwner, fencingToken, out var record) ||
                record.Status != ScheduleStatus.Dispatching) return Task.FromResult(false);
            record.Status = ScheduleStatus.Completed;
            record.CompletedAtUtc = completedAtUtc.ToUniversalTime();
            ClearLease(record);
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<bool> CompleteNativeAsync(Guid scheduleId, string? resourceId, SchedulingStrategy strategy,
        DateTimeOffset acceptedAtUtc, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_records.TryGetValue(scheduleId, out var record)) return Task.FromResult(false);
            if (record.Status == ScheduleStatus.Completed) return Task.FromResult(true);
            if (record.Status != ScheduleStatus.Pending) return Task.FromResult(false);
            record.CreatedResourceId = resourceId;
            record.Strategy = strategy;
            record.Status = ScheduleStatus.Completed;
            record.CompletedAtUtc = acceptedAtUtc.ToUniversalTime();
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<bool> FailAsync(Guid scheduleId, string leaseOwner, long fencingToken, string error,
        DateTimeOffset? retryAtUtc, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!TryGetOwned(scheduleId, leaseOwner, fencingToken, out var record)) return Task.FromResult(false);
            record.AttemptCount++;
            record.LastError = error;
            record.NextAttemptAtUtc = retryAtUtc?.ToUniversalTime();
            record.Status = retryAtUtc.HasValue ? ScheduleStatus.RetryPending : ScheduleStatus.Failed;
            ClearLease(record);
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<bool> CancelAsync(Guid scheduleId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_records.TryGetValue(scheduleId, out var record)) return Task.FromResult(false);
            if (record.Status == ScheduleStatus.Cancelled) return Task.FromResult(true);
            if (record.Status == ScheduleStatus.Completed || record.Status == ScheduleStatus.Dispatching)
                return Task.FromResult(false);
            record.Status = ScheduleStatus.Cancelled;
            ClearLease(record);
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<bool> RescheduleAsync(Guid scheduleId, DateTimeOffset dueAtUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_records.TryGetValue(scheduleId, out var record)) return Task.FromResult(false);
            if (record.Status == ScheduleStatus.Completed || record.Status == ScheduleStatus.Dispatching ||
                record.Status == ScheduleStatus.Failed) return Task.FromResult(false);
            record.DueAtUtc = dueAtUtc.ToUniversalTime();
            record.NextAttemptAtUtc = null;
            record.Status = ScheduleStatus.Pending;
            ClearLease(record);
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<long> CountActiveByResourceAsync(string resourceId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return Task.FromResult((long)_records.Values.Count(record =>
                string.Equals(record.CreatedResourceId, resourceId, StringComparison.Ordinal) && IsActive(record.Status)));
    }

    /// <inheritdoc />
    public Task<long> CountActiveByDestinationAsync(string destination,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return Task.FromResult((long)_records.Values.Count(record =>
                string.Equals(record.Destination, destination, StringComparison.Ordinal) && IsActive(record.Status)));
    }

    private bool TryGetOwned(Guid scheduleId, string leaseOwner, long fencingToken, out ScheduleRecord record)
    {
        if (_records.TryGetValue(scheduleId, out var candidate) &&
            string.Equals(candidate.LeaseOwner, leaseOwner, StringComparison.Ordinal) &&
            candidate.FencingToken == fencingToken)
        {
            record = candidate;
            return true;
        }
        record = new ScheduleRecord();
        return false;
    }

    private static bool IsClaimable(ScheduleRecord record, DateTimeOffset nowUtc)
    {
        if (record.Status == ScheduleStatus.Pending) return record.DueAtUtc <= nowUtc;
        if (record.Status == ScheduleStatus.RetryPending) return (record.NextAttemptAtUtc ?? record.DueAtUtc) <= nowUtc;
        return (record.Status == ScheduleStatus.Claimed || record.Status == ScheduleStatus.Dispatching) &&
               record.LeaseUntilUtc <= nowUtc;
    }

    private static bool IsActive(ScheduleStatus status) => status == ScheduleStatus.Pending ||
        status == ScheduleStatus.Claimed || status == ScheduleStatus.Dispatching || status == ScheduleStatus.RetryPending;

    private static void ClearLease(ScheduleRecord record)
    {
        record.LeaseOwner = null;
        record.LeaseUntilUtc = null;
    }

    private static void EnsureSameRequest(ScheduleRecord existing, ScheduleRecord candidate)
    {
        if (existing.MessageId != candidate.MessageId ||
            !string.Equals(existing.MessageType, candidate.MessageType, StringComparison.Ordinal) ||
            existing.MessageKind != candidate.MessageKind ||
            !string.Equals(existing.Destination, candidate.Destination, StringComparison.Ordinal) ||
            !string.Equals(existing.IdempotencyKey, candidate.IdempotencyKey, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"ScheduleId '{candidate.ScheduleId}' is already associated with a different scheduling request.");
    }

    private static ScheduleRecord Clone(ScheduleRecord source) => new()
    {
        ScheduleId = source.ScheduleId,
        MessageId = source.MessageId,
        RequestId = source.RequestId,
        CorrelationId = source.CorrelationId,
        CausationId = source.CausationId,
        ParentMessageId = source.ParentMessageId,
        SagaId = source.SagaId,
        ResponseEndpoint = source.ResponseEndpoint,
        MessageType = source.MessageType,
        MessageKind = source.MessageKind,
        Destination = source.Destination,
        DueAtUtc = source.DueAtUtc,
        ScheduledAtUtc = source.ScheduledAtUtc,
        Status = source.Status,
        AttemptCount = source.AttemptCount,
        NextAttemptAtUtc = source.NextAttemptAtUtc,
        LeaseOwner = source.LeaseOwner,
        LeaseUntilUtc = source.LeaseUntilUtc,
        FencingToken = source.FencingToken,
        LastError = source.LastError,
        CompletedAtUtc = source.CompletedAtUtc,
        Transport = source.Transport,
        Strategy = source.Strategy,
        Payload = source.Payload.ToArray(),
        Headers = new Dictionary<string, object?>(source.Headers, StringComparer.OrdinalIgnoreCase),
        RequestPayload = source.RequestPayload?.ToArray(),
        RequestType = source.RequestType,
        RequestHeaders = source.RequestHeaders == null
            ? null
            : new Dictionary<string, object?>(source.RequestHeaders, StringComparer.OrdinalIgnoreCase),
        CreatedResourceId = source.CreatedResourceId,
        IsPredefined = source.IsPredefined,
        DelaySuffix = source.DelaySuffix,
        IdempotencyKey = source.IdempotencyKey
    };
}
