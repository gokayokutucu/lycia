// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
namespace Lycia.Saga.Abstractions.Outbox;

/// <summary>Outcome counts from one <see cref="IOutboxDispatcher.DispatchPendingBatchAsync"/> call.</summary>
public class OutboxDispatchResult
{
    public int Claimed { get; set; }
    public int Published { get; set; }
    public int ConfirmationUnknown { get; set; }
    public int Failed { get; set; }
}
