// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
namespace Lycia.Extensions.Configurations;

/// <summary>
/// Configures the optional Inbox. These are provider option values only — Inbox activation itself is
/// always an explicit code-first <c>UsePersistence().With...Inbox()</c> call, never inferred from configuration.
/// </summary>
public class InboxOptions
{
    /// <summary>Gets the configuration section used to bind Inbox options.</summary>
    public static string SectionName => "Lycia:Persistence:Inbox";

    /// <summary>Gets or sets the provider connection string, when the selected Inbox provider needs one.</summary>
    public string? ConnectionString { get; set; }

    /// <summary>Gets or sets how long completed/failed Inbox records are retained before cleanup.</summary>
    public TimeSpan? RetentionPeriod { get; set; }

    /// <summary>
    /// Gets or sets how long an Inbox claim may stay in <c>Processing</c>, or a record in <c>Failed</c>,
    /// before another delivery of the same message is allowed to claim it again. Defaults to 5 minutes.
    /// </summary>
    /// <remarks>
    /// Without this recovery window a process that dies after committing its claim but before completing
    /// the handler leaves the record in <c>Processing</c> permanently, and every later redelivery is
    /// skipped as a duplicate — silently dropping the work. It equally makes a <c>Failed</c> record
    /// retryable, so a message replayed from a dead-letter queue is actually reprocessed instead of being
    /// suppressed forever. <c>Completed</c> is never reclaimable: suppressing duplicates of successfully
    /// committed work is the Inbox's whole purpose.
    /// <para>
    /// Configure this longer than the slowest expected handler execution (including middleware retries).
    /// Setting it shorter than a legitimately long-running handler lets a concurrent redelivery take the
    /// claim over while the first execution is still in flight, which would run the handler twice.
    /// </para>
    /// </remarks>
    public TimeSpan ClaimRecoveryTimeout { get; set; } = TimeSpan.FromMinutes(5);
}
