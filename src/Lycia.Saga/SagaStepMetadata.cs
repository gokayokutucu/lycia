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
}