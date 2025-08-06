using Lycia.Messaging.Utility;

namespace Lycia.Messaging;

public abstract class EventBase : IEvent
{
    protected EventBase()
    {
        SagaId = Guid.Empty;
        MessageId = GuidV7.NewGuidV7();
        ParentMessageId = Guid.Empty; // CausationId
        CorrelationId = MessageId;
        Timestamp = DateTime.UtcNow;
        ApplicationId  = EventMetadata.ApplicationId;
    }
    
    protected EventBase(Guid? sagaId = null)
    {
        SagaId = sagaId;
        MessageId = GuidV7.NewGuidV7();
        ParentMessageId = Guid.Empty; // CausationId
        CorrelationId = MessageId;
        Timestamp = DateTime.UtcNow;
        ApplicationId  = EventMetadata.ApplicationId;
    }
    
    protected EventBase(Guid? sagaId = null, Guid? parentMessageId = null, Guid? correlationId = null)
    {
        SagaId = sagaId;
        MessageId = GuidV7.NewGuidV7();
        ParentMessageId = parentMessageId ?? Guid.Empty; // CausationId
        CorrelationId = correlationId ?? MessageId;
        Timestamp = DateTime.UtcNow;
        ApplicationId  = EventMetadata.ApplicationId;
    }

    public Guid MessageId { get; set; }
    public Guid ParentMessageId { get; set; } // CausationId
    public Guid CorrelationId { get; set; }

    public DateTime Timestamp { get; set; }
    public string ApplicationId { get; set; }
    public Guid? SagaId { get; set; }
}

public abstract class FailedEventBase(string reason) : EventBase
{
    public string Reason { get; private set; } = reason;
}