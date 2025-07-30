using Lycia.Messaging.Enums;
using Lycia.Messaging.Utility;
using Newtonsoft.Json;

namespace Lycia.Messaging;

public class CommandBase: ICommand
{
    protected CommandBase()
    {
        SagaId = Guid.Empty;
        MessageId = GuidV7.NewGuidV7();
        ParentMessageId = Guid.Empty; // CausationId
        CorrelationId = MessageId;
        Timestamp = DateTime.UtcNow;
        ApplicationId  = EventMetadata.ApplicationId;
    }
    
    protected CommandBase(Guid? sagaId = null)
    {
        SagaId = sagaId;
        MessageId = GuidV7.NewGuidV7();
        ParentMessageId = Guid.Empty; // CausationId
        CorrelationId = MessageId;
        Timestamp = DateTime.UtcNow;
        ApplicationId  = EventMetadata.ApplicationId;
    }

    
    protected CommandBase(Guid? sagaId = null, Guid? parentMessageId = null, Guid? correlationId = null)
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