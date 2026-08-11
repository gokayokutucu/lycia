// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0

namespace Lycia.Saga.Abstractions.Outbox;

/// <summary>Controls the hosted Outbox dispatch loop.</summary>
public sealed class OutboxWorkerOptions
{
    /// <summary>Gets or sets whether the registered worker dispatches captured messages.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets the maximum number of rows claimed per pass.</summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>Gets or sets the maximum dispatch attempts for one stable message identity.</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>
    /// Gets or sets how long an in-flight claim may remain unchanged before another replica can
    /// recover it. Configure this longer than the transport's maximum publish timeout.
    /// </summary>
    public TimeSpan RecoveryTimeout { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Gets or sets the normal idle polling interval.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Gets or sets the initial delay after an unconfirmed or failed pass.</summary>
    public TimeSpan RetryBackoff { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Gets or sets the maximum retry delay.</summary>
    public TimeSpan MaxRetryBackoff { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets or sets the maximum random delay added to retry backoff.</summary>
    public TimeSpan MaxJitter { get; set; } = TimeSpan.FromMilliseconds(250);
}
