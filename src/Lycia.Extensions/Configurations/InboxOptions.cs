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
}
