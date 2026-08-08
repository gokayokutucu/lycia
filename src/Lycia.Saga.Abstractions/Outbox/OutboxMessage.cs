// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Common.SagaSteps;

namespace Lycia.Saga.Abstractions.Outbox;

/// <summary>A durably captured outgoing message, awaiting broker dispatch.</summary>
public class OutboxMessage
{
    public OutboxMessage(Guid messageId, string messageTypeName, string payload, string? applicationId, Guid? sagaId)
    {
        MessageId = messageId;
        MessageTypeName = messageTypeName;
        Payload = payload;
        ApplicationId = applicationId;
        SagaId = sagaId;
        Status = OutboxMessageStatus.Pending;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    /// <summary>Stable identity of the outgoing message. Must remain the same value across retries/recovery.</summary>
    public Guid MessageId { get; }

    /// <summary>CLR type name of the captured message, used to deserialize <see cref="Payload"/> on publish.</summary>
    public string MessageTypeName { get; }

    /// <summary>JSON-serialized message payload.</summary>
    public string Payload { get; }

    public string? ApplicationId { get; }

    public Guid? SagaId { get; }

    public OutboxMessageStatus Status { get; set; }

    public SagaStepFailureInfo? FailureInfo { get; set; }

    public DateTime CreatedAtUtc { get; }

    public DateTime UpdatedAtUtc { get; set; }
}
