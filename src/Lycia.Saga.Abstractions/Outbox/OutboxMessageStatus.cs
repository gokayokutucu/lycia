// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
namespace Lycia.Saga.Abstractions.Outbox;

/// <summary>Durable publication lifecycle of an outgoing message captured by <see cref="IOutboxStore"/>.</summary>
public enum OutboxMessageStatus
{
    /// <summary>Captured durably; not yet claimed by a publisher.</summary>
    Pending,

    /// <summary>Claimed by a publisher worker for dispatch. Not yet visible to other workers.</summary>
    Claimed,

    /// <summary>A publish attempt to the broker is in flight.</summary>
    Publishing,

    /// <summary>The broker returned a positive publish confirmation.</summary>
    Published,

    /// <summary>The publish may have succeeded but confirmation was lost (e.g. connection dropped after send). Never auto-promoted to Published.</summary>
    ConfirmationUnknown,

    /// <summary>Publishing failed and will not be retried automatically.</summary>
    Failed,

    /// <summary>
    /// Terminal: the bounded dispatch attempts were exhausted without a positive broker confirmation.
    /// The delivery outcome is genuinely unknown — the message may have reached the broker on one of the
    /// attempts, or never at all — so this is deliberately distinct from both
    /// <see cref="Published"/> and <see cref="Failed"/> (which means a permanent local error before the
    /// transport was reached). An abandoned message is never dispatched again automatically and requires
    /// operator action; it exists so exhausted work is discoverable instead of sitting indefinitely in a
    /// non-terminal state that no worker will ever claim again.
    /// </summary>
    Abandoned
}
