// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using Lycia.Common.Enums;
using Lycia.Common.Helpers;

namespace Lycia.Common.SagaSteps;

public class SagaStepMetadata
{
    public Guid MessageId { get; set; }
    public Guid? ParentMessageId { get; set; }
    public StepStatus Status { get; set; }
    public string MessageTypeName { get; set; } = null!;
    public string? ApplicationId { get; set; } = null!; // Optional but useful
    public string MessagePayload { get; set; } = null!;

    public SagaStepFailureInfo? FailureInfo { get; set; }
    
    public DateTime RecordedAt { get; private set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Creates a SagaStepMetadata instance from inputs.
    /// </summary>
    public static SagaStepMetadata Build(
        StepStatus status,
        Guid messageId,
        Guid? parentMessageId,
        string messageTypeName,
        string? applicationId,
        object? payload,
        SagaStepFailureInfo? failureInfo)
    {
        return new SagaStepMetadata
        {
            Status = status,
            MessageId = messageId,
            ParentMessageId = parentMessageId,
            MessageTypeName = messageTypeName,
            ApplicationId = applicationId,
            MessagePayload = JsonHelper.SerializeSafe(payload),
            FailureInfo = failureInfo
        };
    }
    
    /// <summary>
    /// Determines whether this step is idempotent with another step.
    /// All relevant metadata fields must be exactly equal.
    /// Do not override the equality operator, as this is used for idempotency checks. And we don't involve the RecordedAt timestamp in idempotency checks.
    /// </summary>
    public bool IsIdempotentWith(SagaStepMetadata other)
    {
        return MessageId == other.MessageId
               && (ParentMessageId ?? Guid.Empty) == (other.ParentMessageId ?? Guid.Empty)
               && Status == other.Status
               && MessageTypeName == other.MessageTypeName
               && ApplicationId == other.ApplicationId
               && MessagePayload == other.MessagePayload
               && (FailureInfo?.Equals(other.FailureInfo) ?? other.FailureInfo == null);
    }
}