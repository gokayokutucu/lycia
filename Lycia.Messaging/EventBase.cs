using Lycia.Messaging.Enums;
using Lycia.Messaging.Utility;

namespace Lycia.Messaging;

public abstract class EventBase : IEvent
{
    public Guid MessageId { get; init; } = Guid.NewGuid();
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string ApplicationId { get; init; } = EventMetadata.ApplicationId;
    
    /// <summary>
    /// Gets or sets the unique identifier for the saga instance this event is part of.
    /// </summary>
    public Guid SagaId { get; set; }

#if UNIT_TESTING
    public StepStatus? __TestStepStatus { get; set; }
    public string? __TestStepType { get; set; }
#endif
}

public abstract class FailedEventBase(string reason) : EventBase
{
    public string Reason { get; init; } = reason;
}