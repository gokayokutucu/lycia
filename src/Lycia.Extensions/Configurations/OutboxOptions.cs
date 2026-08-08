// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
namespace Lycia.Extensions.Configurations;

/// <summary>
/// Configures the optional Outbox. These are provider option values only — Outbox activation itself is
/// always an explicit code-first <c>UsePersistence().With...Outbox()</c> call, never inferred from configuration.
/// </summary>
public class OutboxOptions
{
    /// <summary>Gets the configuration section used to bind Outbox options.</summary>
    public static string SectionName => "Lycia:Persistence:Outbox";

    /// <summary>Gets or sets the provider connection string, when the selected Outbox provider needs one.</summary>
    public string? ConnectionString { get; set; }

    /// <summary>Gets or sets how long published Outbox records are retained before cleanup.</summary>
    public TimeSpan? RetentionPeriod { get; set; }
}
