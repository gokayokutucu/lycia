using Lycia.Messaging.Enums;

namespace Lycia.Saga;

public class SagaStepMetadata
{
    public Guid MessageId { get; set; }
    public Guid? ParentMessageId { get; set; }
    public StepStatus Status { get; set; }
    public string MessageTypeName { get; set; } = null!;
    public string ApplicationId { get; set; } = null!; // Optional but useful
    public string MessagePayload { get; set; } = null!;
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
    
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