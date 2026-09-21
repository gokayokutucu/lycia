// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
namespace Lycia.Saga.Abstractions.Outbox;

/// <summary>Outcome counts from one <see cref="IOutboxDispatcher.DispatchPendingBatchAsync"/> call.</summary>
public class OutboxDispatchResult
{
    /// <summary>Gets or sets how many messages this pass claimed for dispatch.</summary>
    public int Claimed { get; set; }

    /// <summary>Gets or sets how many messages the broker positively confirmed.</summary>
    public int Published { get; set; }

    /// <summary>Gets or sets how many messages completed an attempt without a confirmation and remain retryable.</summary>
    public int ConfirmationUnknown { get; set; }

    /// <summary>Gets or sets how many messages failed permanently before reaching the transport.</summary>
    public int Failed { get; set; }

    /// <summary>
    /// Gets or sets how many messages exhausted their bounded attempts without a confirmation and were
    /// moved to the terminal <see cref="OutboxMessageStatus.Abandoned"/> state. A non-zero value means
    /// outgoing business intent was not confirmed as delivered and needs operator attention.
    /// </summary>
    public int Abandoned { get; set; }
}
