// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
namespace Lycia.Extensions.Configurations;

/// <summary>Configures a transport-backed Lycia event bus.</summary>
public class EventBusOptions
{
    /// <summary>Gets or sets the configuration section used to bind event-bus options.</summary>
    public static string SectionName { get; set; } = "Lycia:EventBus";
    /// <summary>Gets or sets the application identity, normalized before it is used in topology names.</summary>
    public string? ApplicationId { get; set; }
    /// <summary>Gets or sets the lifetime of messages in their primary queues.</summary>
    public TimeSpan? MessageTTL { get; set; } = TimeSpan.FromSeconds(Constants.Ttl);
    /// <summary>Gets or sets the dead-letter lifetime, which must not exceed <see cref="MessageTTL"/>.</summary>
    public TimeSpan? DeadLetterQueueMessageTTL { get; set; } = TimeSpan.FromSeconds(Constants.Ttl);
    /// <summary>Gets or sets the registered transport provider name.</summary>
    public string? Provider { get; set; }
    /// <summary>Gets or sets the provider connection string.</summary>
    public string? ConnectionString { get; set; }
}
