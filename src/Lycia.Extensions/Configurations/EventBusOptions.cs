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

    /// <summary>
    /// Gets or sets whether the RabbitMQ transport uses publisher confirms (default <c>true</c>). When enabled,
    /// every publish waits for RabbitMQ's server-side confirmation, unroutable commands and responses are
    /// returned instead of silently dropped, and the Outbox records confirmed publishes as
    /// <c>Published</c>. When disabled the transport publishes fire-and-forget as before, and the Outbox
    /// records an accepted publish as <c>ConfirmationUnknown</c>. Other transports ignore this setting.
    /// </summary>
    /// <remarks>
    /// A confirm means RabbitMQ accepted responsibility for the message according to its documented
    /// semantics. It does not mean any consumer received or processed it.
    /// </remarks>
    public bool PublisherConfirms { get; set; } = true;

    /// <summary>
    /// Gets or sets how long a publish waits for RabbitMQ's confirmation before its outcome is reported as
    /// unknown (default 30 seconds). Configure the Outbox <c>RecoveryTimeout</c> longer than this value.
    /// Only used while <see cref="PublisherConfirms"/> is enabled.
    /// </summary>
    public TimeSpan PublisherConfirmTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets whether an event that no queue receives is treated as a publish failure (default
    /// <c>false</c>). Commands and responses always require a route while <see cref="PublisherConfirms"/> is
    /// enabled, because each has exactly one owner queue. An event may legitimately have no subscribers, so
    /// by default RabbitMQ confirms it and the exchange drops it. Enable this to have such events returned as
    /// unroutable, for example to make the Outbox retry until a subscriber's queue exists.
    /// </summary>
    public bool RequireRoutableEvents { get; set; }
}
