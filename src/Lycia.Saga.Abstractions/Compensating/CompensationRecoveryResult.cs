// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
namespace Lycia.Saga.Abstractions.Compensating;

/// <summary>Outcome counts from one <c>CompensationWorker.RunOnceAsync</c> recovery pass.</summary>
public class CompensationRecoveryResult
{
    /// <summary>Gets or sets how many propagation edges this pass claimed for a recovery attempt.</summary>
    public int Claimed { get; set; }

    /// <summary>Gets or sets how many claimed edges completed parent invocation and were marked <c>Completed</c>.</summary>
    public int Succeeded { get; set; }

    /// <summary>
    /// Gets or sets how many claimed edges failed this attempt (the parent handler threw, or the edge was
    /// structurally unresolvable). A structurally unresolvable edge is already terminal
    /// (<c>CompensationPropagationStatus.Failed</c>) by the time it is counted here; a transient failure
    /// remains <c>Claimed</c> and is retried by a future pass once its lease goes stale, bounded by
    /// <see cref="CompensationWorkerOptions.MaxAttempts"/>.
    /// </summary>
    public int Failed { get; set; }
}
