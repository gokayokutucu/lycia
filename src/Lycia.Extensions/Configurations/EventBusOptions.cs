// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
namespace Lycia.Extensions.Configurations;

public class EventBusOptions
{
    public static string SectionName { get; set; } = "Lycia:EventBus";
    public string? ApplicationId { get; set; }
    public TimeSpan? MessageTTL { get; set; } = TimeSpan.FromSeconds(Constants.Ttl);
    //Dead Letter Queue (DLQ) TTL must be same or no longer than MessageTTL
    public TimeSpan? DeadLetterQueueMessageTTL { get; set; } = TimeSpan.FromSeconds(Constants.Ttl);
    public string? Provider { get; set; }
    public string? ConnectionString { get; set; }
}