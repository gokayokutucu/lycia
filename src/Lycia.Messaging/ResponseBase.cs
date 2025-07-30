using Lycia.Messaging.Enums;
using Lycia.Messaging.Extensions;
using Lycia.Messaging.Utility;
using Newtonsoft.Json;

namespace Lycia.Messaging;

public abstract class ResponseBase<TPrevious> :
    ISuccessResponse<TPrevious>,
    IFailResponse<TPrevious>,
    IEvent
    where TPrevious : IMessage
{
    protected ResponseBase(Guid? parentMessageId = null, Guid? correlationId = null)
    {
#if NET9_0_OR_GREATER
        MessageId = Guid.CreateVersion7(); 
#else
        MessageId = GuidV7.NewGuidV7();
#endif
        ParentMessageId = parentMessageId ?? Guid.Empty;
        CorrelationId = correlationId ?? MessageId;
        Timestamp = DateTime.UtcNow;
        ApplicationId  = EventMetadata.ApplicationId;
    }

#if NETSTANDARD2_0
    public Guid MessageId { get; set; }
    public Guid ParentMessageId { get; set; }
    public DateTime Timestamp { get; set; }
    public string ApplicationId { get; set; }
    public Guid CorrelationId { get; set; }
#else
    public Guid MessageId { get; init; }
    public Guid ParentMessageId { get; init; }
    public DateTime Timestamp { get; init; }
    public string ApplicationId { get; init; }
    public Guid CorrelationId { get; init; } 
#endif
    public Guid? SagaId { get; set; }
}