// TargetFramework: netstandard2.0

namespace Lycia.Scheduling;

public sealed class ScheduleRequest
{
    public string ApplicationId { get; private set; }
    public Type MessageType { get; private set; }            // must implement IMessage
    public byte[] Payload { get; private set; }              // serialized IMessage
    public DateTimeOffset DueTime { get; private set; }
    public Guid CorrelationId { get; private set; }
    public Guid MessageId { get; private set; }
    public IDictionary<string, object> Headers { get; private set; }

    public ScheduleRequest(string applicationId, Type messageType, byte[] payload, DateTimeOffset dueTime, Guid correlationId, Guid messageId, IDictionary<string, object>? headers= default)
    {
        ApplicationId = applicationId;
        MessageType = messageType;
        Payload = payload;
        DueTime = dueTime;
        CorrelationId = correlationId;
        MessageId = messageId;
        Headers = headers ??  new Dictionary<string, object>();
    }    
    
    public ScheduleRequest(string applicationId, Type messageType, byte[] payload, DateTimeOffset dueTime, Guid correlationId, IDictionary<string, object>? headers = default)
    {
        ApplicationId = applicationId;
        MessageType = messageType;
        Payload = payload;
        DueTime = dueTime;
        CorrelationId = correlationId;
        Headers = headers ??  new Dictionary<string, object>();
        MessageId = Guid.NewGuid();
    }
}