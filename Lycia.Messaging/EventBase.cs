using System.Text.Json.Serialization;
using Lycia.Messaging.Enums;
using Lycia.Messaging.Utility;

// using MediatR; // Removed as INotification is no longer implemented here

namespace Lycia.Messaging;

public abstract class EventBase : IEvent // Removed INotification
{
    public Guid MessageId { get; init; } = Guid.NewGuid();
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string ApplicationId { get; init; } = EventMetadata.ApplicationId;
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