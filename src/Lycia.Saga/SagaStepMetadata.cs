using Lycia.Messaging.Enums;
using Lycia.Saga.Helpers;

namespace Lycia.Saga;

public class SagaStepMetadata
{
    public Guid MessageId { get; set; }
    public Guid? ParentMessageId { get; set; }
    public StepStatus Status { get; set; }
    public string MessageTypeName { get; set; } = null!;
    public string ApplicationId { get; set; } = null!; // Optional but useful
    public string MessagePayload { get; set; } = null!;
    public DateTime RecordedAt { get; private set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Creates a SagaStepMetadata instance from inputs.
    /// </summary>
    public static SagaStepMetadata Build(
        StepStatus status,
        Guid messageId,
        Guid? parentMessageId,
        string messageTypeName,
        string applicationId,
        object? payload)
    {
        return new SagaStepMetadata
        {
            Status = status,
            MessageId = messageId,
            ParentMessageId = parentMessageId,
            MessageTypeName = messageTypeName,
            ApplicationId = applicationId,
            MessagePayload = JsonHelper.SerializeSafe(payload)
        };
    }
    
    /// <summary>
    /// Determines whether this step is idempotent with another step.
    /// All relevant metadata fields must be exactly equal.
    /// </summary>
    public bool IsIdempotentWith(SagaStepMetadata other)
    {
        return MessageId == other.MessageId
               && (ParentMessageId ?? Guid.Empty) == (other.ParentMessageId ?? Guid.Empty)
               && Status == other.Status
               && MessageTypeName == other.MessageTypeName
               && ApplicationId == other.ApplicationId
               && MessagePayload == other.MessagePayload;
    }
}