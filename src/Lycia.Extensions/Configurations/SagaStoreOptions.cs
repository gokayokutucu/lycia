// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
namespace Lycia.Extensions.Configurations;

public class SagaStoreOptions
{
    public static string SectionName => "Lycia:EventStore";
    public string? ApplicationId { get; set; }
    public TimeSpan? StepLogTtl { get; set; }= TimeSpan.FromSeconds(Constants.Ttl);
    public int LogMaxRetryCount { get; set; } = 5;
    public string? Provider { get; set; }
    public string? ConnectionString { get; set; }
}