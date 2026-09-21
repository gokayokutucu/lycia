// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Common.SagaSteps;

namespace Lycia.Saga.Abstractions.Inbox;

/// <summary>
/// Tracks committed processing identity of incoming messages, independent of saga step/version
/// semantics. Prevents a handler from executing twice for the same redelivered message
/// (at-least-once transport delivery). Keyed by (MessageId, HandlerType) — the same message may be
/// legitimately processed once per distinct handler.
/// </summary>
/// <remarks>
/// Inbox does not replace <see cref="ISagaStore"/> step logging: the SagaStore tracks saga-state
/// transitions and compensation; the Inbox tracks whether a given handler has already run for a
/// given message, before any saga-state work begins. Optional and disabled by default — a
/// <c>null</c>/unregistered <see cref="IInboxStore"/> means Inbox protection is not active.
/// </remarks>
public interface IInboxStore
{
    /// <summary>
    /// Attempts to claim (MessageId, HandlerType) for processing. Idempotent: repeated calls for the
    /// same pair return the pair's current terminal/in-progress state instead of claiming again.
    /// </summary>
    /// <remarks>
    /// A <c>Completed</c> record is never reclaimable — suppressing duplicates of successfully committed
    /// work is the Inbox's purpose. A record stuck in <c>Processing</c> (a process that died after
    /// committing its claim but before finishing the handler) or sitting in <c>Failed</c> becomes
    /// claimable again once it is older than the provider's configured claim-recovery window, and the
    /// takeover must be atomic so concurrent redeliveries cannot all decide to process the same message.
    /// Without that recovery the first crash or handler failure would make every later redelivery of
    /// that message a permanent no-op, silently dropping the work.
    /// </remarks>
    Task<InboxBeginResult> TryBeginAsync(Guid messageId, Type handlerType, CancellationToken cancellationToken = default);

    /// <summary>Marks a previously-started (MessageId, HandlerType) pair as successfully processed.</summary>
    Task MarkCompletedAsync(Guid messageId, Type handlerType, CancellationToken cancellationToken = default);

    /// <summary>Marks a previously-started (MessageId, HandlerType) pair as failed.</summary>
    Task MarkFailedAsync(Guid messageId, Type handlerType, SagaStepFailureInfo? failureInfo, CancellationToken cancellationToken = default);

    /// <summary>Returns the current status for (MessageId, HandlerType), or <see cref="InboxMessageStatus.None"/> if no record exists.</summary>
    Task<InboxMessageStatus> GetStatusAsync(Guid messageId, Type handlerType, CancellationToken cancellationToken = default);
}
