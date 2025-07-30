using Lycia.Messaging.Enums;
using Lycia.Messaging.Extensions;
using Lycia.Messaging.Utility;
using Newtonsoft.Json;

namespace Lycia.Messaging;

public abstract class EventBase : IEvent
{
    protected EventBase()
    {
        SagaId = Guid.Empty;
#if NET9_0_OR_GREATER
        MessageId = Guid.CreateVersion7(); 
#else
        MessageId = GuidV7.NewGuidV7();
#endif
        ParentMessageId = Guid.Empty; // CausationId
        CorrelationId = MessageId;
        Timestamp = DateTime.UtcNow;
        ApplicationId = EventMetadata.ApplicationId;
    }

    protected EventBase(Guid? sagaId = null)
    {
        SagaId = sagaId;
#if NET9_0_OR_GREATER
        MessageId = Guid.CreateVersion7(); 
#else
        MessageId = GuidV7.NewGuidV7();
#endif
        ParentMessageId = Guid.Empty; // CausationId
        CorrelationId = MessageId;
        Timestamp = DateTime.UtcNow;
        ApplicationId = EventMetadata.ApplicationId;
    }

    protected EventBase(Guid? sagaId = null, Guid? parentMessageId = null, Guid? correlationId = null)
    {
        SagaId = sagaId;
#if NET9_0_OR_GREATER
        MessageId = Guid.CreateVersion7(); 
#else
        MessageId = GuidV7.NewGuidV7();
#endif
        ParentMessageId = parentMessageId ?? Guid.Empty; // CausationId
        CorrelationId = correlationId ?? MessageId;
        Timestamp = DateTime.UtcNow;
        ApplicationId = EventMetadata.ApplicationId;
    }

#if NETSTANDARD2_0
    public Guid MessageId { get; set; }
    public Guid ParentMessageId { get; set; } // CausationId
    public Guid CorrelationId { get; set; }
    public DateTime Timestamp { get; set; }
    public string ApplicationId { get; set; }
#else
    public Guid MessageId { get; init; }
    public Guid ParentMessageId { get; init; } // CausationId
    public Guid CorrelationId { get; init; }
    public DateTime Timestamp { get; init; }
    public string ApplicationId { get; init; }
#endif
    public Guid? SagaId { get; set; }
}

public abstract class FailedEventBase(string reason) : EventBase
{
#if NETSTANDARD2_0
    public string Reason { get; set; } = reason;
#else
    public string Reason { get; init; } = reason;
#endif
}