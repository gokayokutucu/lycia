// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
namespace Lycia.Extensions.Configurations;

/// <summary>Configures persistent saga state and step logging.</summary>
public class SagaStoreOptions
{
    /// <summary>Gets the configuration section used to bind saga-store options.</summary>
    public static string SectionName => "Lycia:EventStore";
    /// <summary>Gets or sets the canonical application identity used to isolate stored saga data.</summary>
    public string? ApplicationId { get; set; }
    /// <summary>Gets or sets how long saga-step log entries are retained.</summary>
    public TimeSpan? StepLogTtl { get; set; }= TimeSpan.FromSeconds(Constants.Ttl);
    /// <summary>Gets or sets the maximum number of attempts for conditional saga-log writes.</summary>
    public int LogMaxRetryCount { get; set; } = 5;
    /// <summary>Gets or sets the registered saga-store provider name.</summary>
    public string? Provider { get; set; }
    /// <summary>Gets or sets the provider connection string.</summary>
    public string? ConnectionString { get; set; }
}
