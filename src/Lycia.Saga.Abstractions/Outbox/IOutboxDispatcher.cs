// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
namespace Lycia.Saga.Abstractions.Outbox;

/// <summary>
/// Reads pending <see cref="IOutboxStore"/> messages, claims them, and publishes them through the
/// application's configured <see cref="IEventBus"/>, applying the conservative
/// Pending → Claimed → Publishing → Published/ConfirmationUnknown/Failed status lifecycle.
/// </summary>
/// <remarks>
/// This is a pull-based, manually-invoked dispatch operation — there is no background hosted-service
/// loop wired in yet. Only <see cref="Lycia.Saga.Abstractions.Messaging.IEvent"/>-typed messages can
/// be redispatched today (via <c>IEventBus.Publish</c>); Outbox-backed Send/Respond routing is future
/// work. Callers currently populate the Outbox themselves via <see cref="IOutboxStore.AddAsync"/> —
/// there is no automatic <c>Context.Publish()</c> interception yet (see README.md's Outbox roadmap).
/// </remarks>
public interface IOutboxDispatcher
{
    /// <summary>
    /// Claims and publishes up to <paramref name="maxCount"/> pending messages. Safe to call
    /// concurrently from multiple workers/processes against the same durable Outbox store — the
    /// underlying store's claim operation ensures only one caller wins each message.
    /// </summary>
    Task<OutboxDispatchResult> DispatchPendingBatchAsync(int maxCount = 50, CancellationToken cancellationToken = default);
}
