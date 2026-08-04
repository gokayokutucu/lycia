// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
namespace Lycia.Extensions.Configurations;

/// <summary>Defines default durations, retry limits, transport header names, and provider identifiers used by Lycia extensions.</summary>
public static class Constants
{
    /// <summary>Default message and saga-step time-to-live, in seconds.</summary>
    public const int Ttl = 3600;
    /// <summary>Default maximum number of attempts when persisting a saga log entry.</summary>
    public const int LogMaxRetryCount = 5;

    /// <summary>Transport header containing the stable saga instance identifier.</summary>
    public const string SagaIdHeader = "SagaId";
    /// <summary>Transport header containing the workflow-wide correlation identifier.</summary>
    public const string CorrelationIdHeader = "CorrelationId";
    /// <summary>Transport header containing the unique identifier of the current message.</summary>
    public const string MessageIdHeader = "MessageId";
    /// <summary>Transport header containing the previous message used for compensation traversal.</summary>
    public const string ParentMessageIdHeader = "ParentMessageId";
    /// <summary>Transport header containing the message that directly caused the current message.</summary>
    public const string CausationIdHeader = "CausationId";
    /// <summary>Transport header containing the message creation timestamp.</summary>
    public const string TimestampHeader = "Timestamp";
    /// <summary>Transport header containing the canonical application identity.</summary>
    public const string ApplicationIdHeader = "ApplicationId";
    /// <summary>Transport header linking a response to the request message identifier.</summary>
    public const string RequestIdHeader = "RequestId";
    /// <summary>Legacy transport header for the response destination; use <see cref="ResponseEndpointHeader"/> for new integrations.</summary>
    public const string ReplyToHeader = "ReplyTo";
    /// <summary>Transport header containing the targeted endpoint to which a response must be delivered.</summary>
    public const string ResponseEndpointHeader = "ResponseEndpoint";
    /// <summary>Transport header containing an event contract identifier.</summary>
    public const string EventTypeHeader = "EventType";
    /// <summary>Transport header containing a command contract identifier.</summary>
    public const string CommandTypeHeader = "CommandType";
    /// <summary>Transport header containing the broker publication timestamp.</summary>
    public const string PublishedAtHeader = "PublishedAt";
    
    /// <summary>Provider identifier for the Redis saga store.</summary>
    public const string ProviderRedis = "Redis";
    /// <summary>Provider identifier for the RabbitMQ event bus.</summary>
    public const string ProviderRabbitMq = "RabbitMQ";

}
