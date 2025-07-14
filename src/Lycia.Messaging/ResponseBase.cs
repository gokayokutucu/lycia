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
        MessageId = GuidExtensions.CreateVersion7();
#endif
        ParentMessageId = parentMessageId ?? Guid.Empty;
        CorrelationId = correlationId ?? MessageId;
        Timestamp = DateTime.UtcNow;
        ApplicationId  = EventMetadata.ApplicationId;
    }

#if NET5_0_OR_GREATER
    public Guid MessageId { get; init; }
    public Guid ParentMessageId { get; init; }
    public DateTime Timestamp { get; init; }
    public string ApplicationId { get; init; }
    public Guid CorrelationId { get; init; } 
#else
    public Guid MessageId { get; set; }
    public Guid ParentMessageId { get; set; }
    public DateTime Timestamp { get; set; }
    public string ApplicationId { get; set; }
    public Guid CorrelationId { get; set; }
#endif
    public Guid? SagaId { get; set; }
#if UNIT_TESTING
    [JsonIgnore]
    public StepStatus? __TestStepStatus { get; set; }
    [JsonIgnore]
    public Type? __TestStepType { get; set; }
#endif
}