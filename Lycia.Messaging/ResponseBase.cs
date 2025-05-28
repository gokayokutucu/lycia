using System.Text.Json.Serialization;
using Lycia.Messaging.Enums;
using Lycia.Messaging.Utility;

namespace Lycia.Messaging;

public abstract class ResponseBase<TPrevious> :
    ISuccessResponse<TPrevious>,
    IFailResponse<TPrevious>,
    IEvent
    where TPrevious : IMessage
{
    public Guid MessageId { get; init; } = Guid.NewGuid(); // Changed to NewGuid()
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string ApplicationId { get; init; } = EventMetadata.ApplicationId;
    public Guid CorrelationId { get; set; }
    public Guid? SagaId { get; set; }
#if UNIT_TESTING
    [JsonIgnore]
    public StepStatus? __TestStepStatus { get; set; }
    [JsonIgnore]
    public Type? __TestStepType { get; set; }
#endif
}