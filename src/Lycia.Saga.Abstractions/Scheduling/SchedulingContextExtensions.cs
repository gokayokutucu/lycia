// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0

using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Contexts;
using Lycia.Saga.Abstractions.Messaging;

namespace Lycia.Saga.Abstractions.Scheduling;

/// <summary>Transport-independent scheduling operations available from a Lycia saga context.</summary>
public static class SchedulingContextExtensions
{
    /// <summary>
    /// Creates a deferred tracked schedule operation for a predefined delay bucket, reachable from any
    /// <see cref="ISagaContext"/>-typed <c>Context</c> property (for example inside a
    /// <c>CoordinatedSagaHandler&lt;TMessage,TSagaData&gt;</c>), mirroring how <see cref="Schedule{TMessage}(ISagaContext,TMessage,ScheduleDelay,CancellationToken)"/>
    /// is reachable for the standalone form. The underlying schedule call is not made until a terminal
    /// method on the returned <see cref="ISagaStepFluent"/> is awaited.
    /// </summary>
    public static ISagaStepFluent ScheduleWithTracking<TMessage>(this ISagaContext context, TMessage message,
        ScheduleDelay delay, CancellationToken cancellationToken = default) where TMessage : IMessage =>
        GetSchedulingContext(context).ScheduleWithTracking(message, delay, cancellationToken);

    /// <summary>Schedules a command, event, or response using a recommended predefined delay bucket.</summary>
    public static Task<Guid> Schedule<TMessage>(this ISagaContext context, TMessage message, ScheduleDelay delay,
        CancellationToken cancellationToken = default) where TMessage : IMessage =>
        GetSchedulingContext(context).ScheduleMessageAsync(message, delay, null, cancellationToken);

    /// <summary>Schedules a message using a caller-provided idempotency key suitable for retrying the surrounding operation.</summary>
    public static Task<Guid> Schedule<TMessage>(this ISagaContext context, TMessage message, ScheduleDelay delay,
        Guid scheduleId, CancellationToken cancellationToken = default) where TMessage : IMessage =>
        GetSchedulingContext(context).ScheduleMessageAsync(message, delay, RequireScheduleId(scheduleId), cancellationToken);

    /// <summary>
    /// Schedules a message after an arbitrary positive duration. Broker strategies that allocate dynamic resources may
    /// make this more expensive than a predefined <see cref="ScheduleDelay"/> bucket.
    /// </summary>
    public static Task<Guid> Schedule<TMessage>(this ISagaContext context, TMessage message, TimeSpan delay,
        CancellationToken cancellationToken = default) where TMessage : IMessage =>
        GetSchedulingContext(context).ScheduleMessageAsync(message, delay, null, cancellationToken);

    /// <summary>Schedules an arbitrary delay with a stable caller-provided ScheduleId.</summary>
    public static Task<Guid> Schedule<TMessage>(this ISagaContext context, TMessage message, TimeSpan delay,
        Guid scheduleId, CancellationToken cancellationToken = default) where TMessage : IMessage =>
        GetSchedulingContext(context).ScheduleMessageAsync(message, delay, RequireScheduleId(scheduleId), cancellationToken);

    /// <summary>Schedules a message for an exact instant; the value is normalized to UTC.</summary>
    public static Task<Guid> ScheduleAt<TMessage>(this ISagaContext context, TMessage message,
        DateTimeOffset dueAtUtc, CancellationToken cancellationToken = default) where TMessage : IMessage =>
        GetSchedulingContext(context).ScheduleMessageAtAsync(message, dueAtUtc, null, cancellationToken);

    /// <summary>Schedules an exact instant with a stable caller-provided ScheduleId.</summary>
    public static Task<Guid> ScheduleAt<TMessage>(this ISagaContext context, TMessage message,
        DateTimeOffset dueAtUtc, Guid scheduleId, CancellationToken cancellationToken = default)
        where TMessage : IMessage =>
        GetSchedulingContext(context).ScheduleMessageAtAsync(message, dueAtUtc, RequireScheduleId(scheduleId), cancellationToken);

    /// <summary>Cancels a pending schedule idempotently.</summary>
    public static Task<bool> CancelSchedule(this ISagaContext context, Guid scheduleId,
        CancellationToken cancellationToken = default) =>
        GetSchedulingContext(context).CancelScheduleAsync(RequireScheduleId(scheduleId), cancellationToken);

    /// <summary>Changes the due time of a schedule that has not completed.</summary>
    public static Task<bool> Reschedule(this ISagaContext context, Guid scheduleId, DateTimeOffset dueAtUtc,
        CancellationToken cancellationToken = default) =>
        GetSchedulingContext(context).RescheduleAsync(RequireScheduleId(scheduleId), dueAtUtc, cancellationToken);

    private static ISchedulingSagaContext GetSchedulingContext(ISagaContext context)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        if (context is ISchedulingSagaContext schedulingContext) return schedulingContext;
        throw new InvalidOperationException(
            $"Saga context '{context.GetType().FullName}' does not provide Lycia scheduling services.");
    }

    private static Guid RequireScheduleId(Guid scheduleId)
    {
        if (scheduleId == Guid.Empty) throw new ArgumentException("ScheduleId cannot be empty.", nameof(scheduleId));
        return scheduleId;
    }
}
