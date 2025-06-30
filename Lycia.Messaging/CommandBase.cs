using Lycia.Messaging.Enums;
using Lycia.Messaging.Extensions;
using Lycia.Messaging.Utility;
using Newtonsoft.Json;

namespace Lycia.Messaging;

public class CommandBase : ICommand
{
    protected CommandBase(Guid? parentMessageId = null, Guid? correlationId = null)
    {
#if NET9_0_OR_GREATER
        MessageId = Guid.CreateVersion7(); 
#else
        MessageId = GuidExtensions.CreateVersion7();
#endif
        ParentMessageId = parentMessageId ?? Guid.Empty;
        CorrelationId = correlationId ?? MessageId;
        Timestamp = DateTime.UtcNow;
        ApplicationId = EventMetadata.ApplicationId;
    }

    public Guid MessageId
    {
        get;
#if NET6_0_OR_GREATER
        init;  
#else
        set;
#endif
    }
    public Guid ParentMessageId
    {
        get;
#if NET6_0_OR_GREATER
        init;  
#else
        private set;
#endif
    }
    public Guid CorrelationId
    {
        get;
#if NET6_0_OR_GREATER
        init;  
#else
        set;
#endif
    }
    public DateTime Timestamp
    {
        get;
#if NET6_0_OR_GREATER
        init;  
#else
        set;
#endif
    }
    public string ApplicationId
    {
        get;
#if NET6_0_OR_GREATER
        init;  
#else
        private set;
#endif
    }
    public Guid? SagaId { get; set; }
#if UNIT_TESTING
    [JsonIgnore]
    public StepStatus? __TestStepStatus { get; set; }
    [JsonIgnore]
    public Type? __TestStepType { get; set; }
#endif
}