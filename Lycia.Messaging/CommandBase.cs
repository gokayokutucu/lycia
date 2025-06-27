using Lycia.Messaging.Enums;
using Lycia.Messaging.Extensions;
using Lycia.Messaging.Utility;
using Newtonsoft.Json;

namespace Lycia.Messaging;

public class CommandBase: ICommand
{
    protected CommandBase(Guid? parentMessageId = null, Guid? correlationId = null)
    {
        MessageId = GuidExtensions.CreateVersion7();
        ParentMessageId = parentMessageId ?? Guid.Empty;
        CorrelationId = correlationId ?? MessageId;
        Timestamp = DateTime.UtcNow;
        ApplicationId  = EventMetadata.ApplicationId;
    }

    public Guid MessageId { get; set;  }
    public Guid ParentMessageId { get; private set;  }
    public Guid CorrelationId { get; set;  }
    public DateTime Timestamp { get; set;  }
    public string ApplicationId { get; private set;  } 
    public Guid? SagaId { get; set; }
#if UNIT_TESTING
    [JsonIgnore]
    public StepStatus? __TestStepStatus { get; set; }
    [JsonIgnore]
    public Type? __TestStepType { get; set; }
#endif
}