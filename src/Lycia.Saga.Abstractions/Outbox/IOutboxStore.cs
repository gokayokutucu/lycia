// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Common.SagaSteps;

namespace Lycia.Saga.Abstractions.Outbox;

/// <summary>
/// Durably records outgoing message intent before broker publication and exposes the lifecycle the
/// dispatcher worker uses to publish it reliably. This contract covers durable capture and status
/// bookkeeping; the publisher worker, bounded retry policy, and broker-confirmation wiring live in
/// <c>IOutboxDispatcher</c>/<c>OutboxWorker</c> and <c>IConfirmedEventBus</c>.
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
    /// Atomically claims up to <paramref name="maxCount"/> dispatchable messages, transitioning them to
    /// <see cref="OutboxMessageStatus.Claimed"/> so a publisher worker can dispatch them without another
    /// worker claiming the same rows.
    /// </summary>
    /// <remarks>
    /// Eligible rows are <see cref="OutboxMessageStatus.Pending"/> ones, plus
    /// <see cref="OutboxMessageStatus.ConfirmationUnknown"/>, <see cref="OutboxMessageStatus.Claimed"/>,
    /// and <see cref="OutboxMessageStatus.Publishing"/> ones whose last update is older than
    /// <paramref name="recoveryTimeout"/>. Applying the recovery window to
    /// <see cref="OutboxMessageStatus.ConfirmationUnknown"/> as well is deliberate: without it an
    /// unconfirmed message is re-claimed on the very next dispatch pass, which burns every attempt
    /// permitted by <paramref name="maxAttempts"/> within seconds and republishes the same message that
    /// many times. Rows that already reached <paramref name="maxAttempts"/> are never returned. When the
    /// last permitted attempt did not reach the transport the dispatcher moves the row to the terminal
    /// <see cref="OutboxMessageStatus.Abandoned"/> state so undelivered work stays discoverable; when the
    /// transport accepted it but could not confirm, the row stays
    /// <see cref="OutboxMessageStatus.ConfirmationUnknown"/> at the cap.
    /// </remarks>
    Task<IReadOnlyList<OutboxMessage>> ClaimPendingBatchAsync(int maxCount, CancellationToken cancellationToken = default,
        int maxAttempts = 5, TimeSpan? recoveryTimeout = null);

    Task MarkPublishingAsync(Guid messageId, CancellationToken cancellationToken = default);

    /// <summary>Marks a message as published. Callers must only do this after a positive broker confirmation.</summary>
    Task MarkPublishedAsync(Guid messageId, CancellationToken cancellationToken = default);

    /// <summary>Marks a message whose publish outcome could not be confirmed (e.g. connection lost mid-publish).</summary>
    Task MarkConfirmationUnknownAsync(Guid messageId, CancellationToken cancellationToken = default);

    Task MarkFailedAsync(Guid messageId, SagaStepFailureInfo? failureInfo, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a message whose last permitted dispatch attempt did not reach the transport, moving it to the
    /// terminal <see cref="OutboxMessageStatus.Abandoned"/> state.
    /// </summary>
    /// <remarks>
    /// This exists so a message that was never handed to the transport cannot silently become invisible:
    /// without it a row sits at <see cref="OutboxMessageStatus.ConfirmationUnknown"/> with its attempt
    /// count at the cap, which no claim query will ever return again, leaving no terminal state and no
    /// recorded reason. Implementations
    /// must record <paramref name="failureInfo"/> and must remove the message from any pending/claimable
    /// queue.
    /// </remarks>
    Task MarkAbandonedAsync(Guid messageId, SagaStepFailureInfo? failureInfo, CancellationToken cancellationToken = default);
}
