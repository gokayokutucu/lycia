using Lycia.Messaging.Utility;

namespace Lycia.Messaging;

public abstract class EventBase : IEvent
{
    public Guid MessageId { get; init; } = Guid.NewGuid();
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string ApplicationId { get; init; } = EventMetadata.ApplicationId;
}

public abstract class FailedEventBase(string reason) : EventBase
{
    public string Reason { get; init; } = reason;
}