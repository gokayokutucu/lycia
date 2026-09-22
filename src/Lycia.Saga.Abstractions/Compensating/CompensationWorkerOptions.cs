// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
namespace Lycia.Saga.Abstractions.Compensating;

/// <summary>
/// Controls the hosted <c>CompensationWorker</c> recovery loop and the immediate in-request propagation
/// attempt <c>ThenBubbleUp</c>/<c>Context.BubbleUpCompensationAsync</c> makes. Registered unconditionally
/// by <c>AddLycia</c> with these defaults - compensation propagation durability is part of SagaStore
/// correctness, not an opt-in capability, so there is no separate "enable compensation" switch. Use
/// <c>LyciaPersistenceBuilder.WithCompensationWorker(...)</c> only to tune these values.
/// </summary>
public sealed class CompensationWorkerOptions
{
    /// <summary>
    /// Gets or sets whether the registered worker runs its recovery loop. This only stops the background
    /// recovery pass; it does not disable the immediate in-request propagation attempt or the durable
    /// intent persisted by <c>ThenBubbleUp</c> - those remain part of SagaStore correctness regardless.
    /// Default <c>true</c>.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets the maximum number of propagation edges claimed per recovery pass.</summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>
    /// Gets or sets the maximum propagation attempts for one edge before it is marked
    /// <see cref="Lycia.Common.SagaSteps.CompensationPropagationStatus.Failed"/>. The immediate in-request
    /// attempt counts as the first attempt.
    /// </summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>
    /// Gets or sets how long a claim may remain unchanged before another owner (the worker, or another
    /// replica's immediate attempt) may recover it. Configure this longer than a compensation handler's
    /// expected worst-case duration.
    /// </summary>
    public TimeSpan RecoveryTimeout { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Gets or sets the normal idle polling interval.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Gets or sets the initial delay after a pass that found no claimable work or encountered a failure.</summary>
    public TimeSpan RetryBackoff { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Gets or sets the maximum retry delay.</summary>
    public TimeSpan MaxRetryBackoff { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets or sets the maximum random delay added to retry backoff.</summary>
    public TimeSpan MaxJitter { get; set; } = TimeSpan.FromMilliseconds(250);
}
