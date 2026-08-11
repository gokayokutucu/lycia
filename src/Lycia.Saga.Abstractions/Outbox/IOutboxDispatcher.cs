// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
namespace Lycia.Saga.Abstractions.Outbox;

/// <summary>
/// Reads pending <see cref="IOutboxStore"/> messages, claims them, and publishes them through the
/// application's configured <see cref="IEventBus"/>, applying the conservative
/// Pending → Claimed → Publishing → Published/ConfirmationUnknown/Failed status lifecycle.
/// </summary>
public interface IOutboxDispatcher
{
    /// <summary>
    /// Claims and publishes up to <paramref name="maxCount"/> pending messages. Safe to call
    /// concurrently from multiple workers/processes against the same durable Outbox store — the
    /// underlying store's claim operation ensures only one caller wins each message.
    /// </summary>
    Task<OutboxDispatchResult> DispatchPendingBatchAsync(int maxCount = 50,
        CancellationToken cancellationToken = default, int maxAttempts = 5, TimeSpan? recoveryTimeout = null);
}
