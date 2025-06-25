using System.Text.Json.Serialization;
using Lycia.Messaging.Enums;
using Lycia.Messaging.Utility;

namespace Lycia.Messaging;

public abstract class EventBase : IEvent
{
    protected EventBase(Guid? parentMessageId = null, Guid? correlationId = null)
    {
        MessageId = Guid.CreateVersion7();
        ParentMessageId = parentMessageId ?? Guid.Empty;
        CorrelationId = correlationId ?? MessageId;
        Timestamp = DateTime.UtcNow;
        ApplicationId  = EventMetadata.ApplicationId;
    }

    public Guid MessageId { get; init; }
    public Guid ParentMessageId { get; init; }
    public Guid CorrelationId { get; init; }
    public DateTime Timestamp { get; init; }
    public string ApplicationId { get; init; }
    public Guid? SagaId { get; set; }
#if UNIT_TESTING
    [JsonIgnore]
    public StepStatus? __TestStepStatus { get; set; }
    [JsonIgnore]
    public Type? __TestStepType { get; set; }
#endif
}

public abstract class FailedEventBase(string reason) : EventBase
{
    public string Reason { get; init; } = reason;
}