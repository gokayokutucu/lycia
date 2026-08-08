// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Common.SagaSteps;

namespace Lycia.Saga.Abstractions.Outbox;

/// <summary>
/// Durably records outgoing message intent before broker publication and exposes the lifecycle
/// needed for a future dispatcher worker to publish it reliably. This contract only covers durable
/// capture and status bookkeeping — the publisher worker, retry policy, and broker-confirmation
/// wiring are a separate, not-yet-implemented concern (see Outbox roadmap in README.md).
/// </summary>
/// <remarks>
/// Optional and disabled by default. Does not by itself provide exactly-once delivery: Lycia
/// remains at-least-once end to end, and a message is never considered <see cref="OutboxMessageStatus.Published"/>
/// without a positive broker confirmation.
/// </remarks>
public interface IOutboxStore
{
    /// <summary>
    /// Durably captures an outgoing message. Idempotent on <see cref="OutboxMessage.MessageId"/>:
    /// re-adding an already-known MessageId is a safe no-op and does not reset its status.
    /// </summary>
    Task AddAsync(OutboxMessage message, CancellationToken cancellationToken = default);

    /// <summary>Retrieves a captured message by id, or <c>null</c> if none exists.</summary>
    Task<OutboxMessage?> GetByMessageIdAsync(Guid messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically claims up to <paramref name="maxCount"/> <see cref="OutboxMessageStatus.Pending"/>
    /// messages, transitioning them to <see cref="OutboxMessageStatus.Claimed"/> so a publisher
    /// worker can dispatch them without another worker claiming the same rows.
    /// </summary>
    Task<IReadOnlyList<OutboxMessage>> ClaimPendingBatchAsync(int maxCount, CancellationToken cancellationToken = default);

    Task MarkPublishingAsync(Guid messageId, CancellationToken cancellationToken = default);

    /// <summary>Marks a message as published. Callers must only do this after a positive broker confirmation.</summary>
    Task MarkPublishedAsync(Guid messageId, CancellationToken cancellationToken = default);

    /// <summary>Marks a message whose publish outcome could not be confirmed (e.g. connection lost mid-publish).</summary>
    Task MarkConfirmationUnknownAsync(Guid messageId, CancellationToken cancellationToken = default);

    Task MarkFailedAsync(Guid messageId, SagaStepFailureInfo? failureInfo, CancellationToken cancellationToken = default);
}
