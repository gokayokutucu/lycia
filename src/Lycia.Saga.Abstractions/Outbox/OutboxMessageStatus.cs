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

    /// <summary>
    /// A publish attempt to the broker is in flight. A row that stays here past the recovery timeout belongs
    /// to a worker that stopped before recording the attempt's outcome; it is recovered, not dropped,
    /// whatever its attempt count.
    /// </summary>
    Publishing,

    /// <summary>The broker returned a positive publish confirmation.</summary>
    Published,

    /// <summary>The publish may have succeeded but confirmation was lost (e.g. connection dropped after send). Never auto-promoted to Published.</summary>
    ConfirmationUnknown,

    /// <summary>Publishing failed and will not be retried automatically.</summary>
    Failed,

    /// <summary>
    /// Terminal: the last permitted dispatch attempt did not reach the transport — the publish threw — or
    /// workers stopped mid-dispatch on the final attempt and again on its recovery attempt, so the message
    /// may never have been delivered. Deliberately
    /// distinct from <see cref="Failed"/>, which means a permanent local error before any publish was
    /// attempted. An abandoned message is never dispatched again automatically and requires operator
    /// action; it exists so exhausted work is discoverable instead of sitting indefinitely in a
    /// non-terminal state that no worker will ever claim again. A final attempt that the transport
    /// accepted but cannot confirm stays <see cref="ConfirmationUnknown"/>: that is the normal outcome
    /// for an unconfirming transport such as Core NATS (or RabbitMQ with publisher confirms disabled), not a failure.
    /// </summary>
    Abandoned
}
