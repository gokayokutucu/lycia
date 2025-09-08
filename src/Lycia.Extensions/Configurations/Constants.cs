// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
namespace Lycia.Extensions.Configurations;

public static class Constants
{
    public const int Ttl = 3600;
    public const int LogMaxRetryCount = 5;

    public const string SagaIdHeader = "SagaId";
    public const string CorrelationIdHeader = "CorrelationId";
    public const string MessageIdHeader = "MessageId";
    public const string ParentMessageIdHeader = "ParentMessageId";
    public const string TimestampHeader = "Timestamp";
    public const string ApplicationIdHeader = "ApplicationId";
    public const string EventTypeHeader = "EventType";
    public const string CommandTypeHeader = "CommandType";
    public const string PublishedAtHeader = "PublishedAt";
    
    public const string ProviderRedis = "Redis";
    public const string ProviderRabbitMq = "RabbitMQ";

}