// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0

using Lycia.Saga.Abstractions.Messaging;

namespace Lycia.Saga.Abstractions.Scheduling;

/// <summary>UTC clock used by scheduling and vacuum components.</summary>
public interface ISchedulingClock
{
    /// <summary>Gets the current UTC instant.</summary>
    DateTimeOffset UtcNow { get; }
}

/// <summary>Durable, atomic schedule persistence contract.</summary>
public interface IScheduleStore
{
    /// <summary>Creates a record once for its ScheduleId, returning the existing request on an idempotent retry.</summary>
    Task<ScheduleCreationResult> CreateAsync(ScheduleRecord record, CancellationToken cancellationToken = default);
    /// <summary>Gets a schedule by id, or null when absent.</summary>
    Task<ScheduleRecord?> GetAsync(Guid scheduleId, CancellationToken cancellationToken = default);
    /// <summary>Atomically claims due records and assigns a new fencing token.</summary>
    Task<IReadOnlyList<ScheduleClaim>> ClaimDueAsync(DateTimeOffset nowUtc, int maximumCount, string leaseOwner,
        TimeSpan leaseDuration, CancellationToken cancellationToken = default);
    /// <summary>Renews a claim only when owner and fencing token still match.</summary>
    Task<bool> RenewLeaseAsync(Guid scheduleId, string leaseOwner, long fencingToken, DateTimeOffset leaseUntilUtc,
        CancellationToken cancellationToken = default);
    /// <summary>Moves a valid claim to dispatching.</summary>
    Task<bool> MarkDispatchingAsync(Guid scheduleId, string leaseOwner, long fencingToken,
        CancellationToken cancellationToken = default);
    /// <summary>Completes a valid claim after broker acceptance.</summary>
    Task<bool> CompleteAsync(Guid scheduleId, string leaseOwner, long fencingToken, DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken = default);
    /// <summary>Marks a newly created request accepted by a broker-native scheduling mechanism.</summary>
    Task<bool> CompleteNativeAsync(Guid scheduleId, string? resourceId, SchedulingStrategy strategy,
        DateTimeOffset acceptedAtUtc, CancellationToken cancellationToken = default);
    /// <summary>Releases a valid claim for retry or terminal failure.</summary>
    Task<bool> FailAsync(Guid scheduleId, string leaseOwner, long fencingToken, string error,
        DateTimeOffset? retryAtUtc, CancellationToken cancellationToken = default);
    /// <summary>Cancels a pending or retrying schedule idempotently.</summary>
    Task<bool> CancelAsync(Guid scheduleId, CancellationToken cancellationToken = default);
    /// <summary>Changes the due time of a non-completed schedule without changing its identity.</summary>
    Task<bool> RescheduleAsync(Guid scheduleId, DateTimeOffset dueAtUtc, CancellationToken cancellationToken = default);
    /// <summary>Counts active schedules associated with a managed transport resource.</summary>
    Task<long> CountActiveByResourceAsync(string resourceId, CancellationToken cancellationToken = default);
    /// <summary>Counts active schedules targeting a canonical destination.</summary>
    Task<long> CountActiveByDestinationAsync(string destination, CancellationToken cancellationToken = default);
}

/// <summary>Creates idempotent schedules without exposing transport-specific routing.</summary>
public interface IMessageScheduler
{
    /// <summary>Schedules a message using a recommended fixed bucket.</summary>
    Task<Guid> ScheduleAsync(IMessage message, IMessage currentMessage, Type handlerType, Guid sagaId,
        ScheduleDelay delay, Guid? scheduleId = null, CancellationToken cancellationToken = default);
    /// <summary>Schedules a message after an arbitrary positive duration.</summary>
    Task<Guid> ScheduleAsync(IMessage message, IMessage currentMessage, Type handlerType, Guid sagaId,
        TimeSpan delay, Guid? scheduleId = null, CancellationToken cancellationToken = default);
    /// <summary>Schedules a message for an exact UTC-normalized instant.</summary>
    Task<Guid> ScheduleAtAsync(IMessage message, IMessage currentMessage, Type handlerType, Guid sagaId,
        DateTimeOffset dueAtUtc, Guid? scheduleId = null, CancellationToken cancellationToken = default);
    /// <summary>Cancels a pending schedule idempotently.</summary>
    Task<bool> CancelAsync(Guid scheduleId, CancellationToken cancellationToken = default);
    /// <summary>Changes the due time of a pending schedule.</summary>
    Task<bool> RescheduleAsync(Guid scheduleId, DateTimeOffset dueAtUtc, CancellationToken cancellationToken = default);
}

/// <summary>Bridge implemented by Lycia contexts to expose scheduling extension methods compatibly.</summary>
public interface ISchedulingSagaContext
{
    /// <summary>Schedules through the context while preserving the current message lineage.</summary>
    Task<Guid> ScheduleMessageAsync(IMessage message, ScheduleDelay delay, Guid? scheduleId,
        CancellationToken cancellationToken);
    /// <summary>Schedules through the context after an arbitrary delay.</summary>
    Task<Guid> ScheduleMessageAsync(IMessage message, TimeSpan delay, Guid? scheduleId,
        CancellationToken cancellationToken);
    /// <summary>Schedules through the context for an absolute instant.</summary>
    Task<Guid> ScheduleMessageAtAsync(IMessage message, DateTimeOffset dueAtUtc, Guid? scheduleId,
        CancellationToken cancellationToken);
    /// <summary>Cancels a pending context schedule.</summary>
    Task<bool> CancelScheduleAsync(Guid scheduleId, CancellationToken cancellationToken);
    /// <summary>Reschedules a pending context schedule.</summary>
    Task<bool> RescheduleAsync(Guid scheduleId, DateTimeOffset dueAtUtc, CancellationToken cancellationToken);
}

/// <summary>Dispatches a due durable record with its original Send, Publish, or Respond semantic.</summary>
public interface ISchedulingDispatcher
{
    /// <summary>Dispatches the record and completes only after transport acceptance.</summary>
    Task DispatchAsync(ScheduleRecord record, CancellationToken cancellationToken = default);
}

/// <summary>Optional broker-native scheduling capability.</summary>
public interface INativeSchedulingTransport
{
    /// <summary>Gets the transport audit name.</summary>
    string TransportName { get; }
    /// <summary>Returns whether this transport can safely schedule the envelope natively.</summary>
    Task<bool> CanScheduleAsync(NativeScheduleEnvelope envelope, CancellationToken cancellationToken = default);
    /// <summary>Schedules the envelope using a validated native mechanism.</summary>
    Task<string?> ScheduleNativeAsync(NativeScheduleEnvelope envelope, CancellationToken cancellationToken = default);
}
