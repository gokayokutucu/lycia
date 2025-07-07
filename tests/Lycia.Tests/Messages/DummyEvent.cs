using Lycia.Messaging;
using Lycia.Messaging.Enums;
using Lycia.Messaging.Utility;

namespace Lycia.Tests.Messages;

// Dummy types for test isolation
public class DummyEvent : IMessage
{
    public DummyEvent()
    {
    }

    public DummyEvent(Guid? parentMessageId = null, Guid? correlationId = null, string? applicationId = null)
    {
        MessageId = Guid.CreateVersion7();
        ParentMessageId = parentMessageId ?? Guid.Empty;
        CorrelationId = correlationId ?? MessageId;
        Timestamp = DateTime.UtcNow;
        ApplicationId = applicationId ?? EventMetadata.ApplicationId;
    }

    public Guid MessageId { get; init; }
    public Guid ParentMessageId { get; init; }
#if NET5_0_OR_GREATER
    public Guid CorrelationId { get; init; }
#else
    public Guid CorrelationId { get; set; }
#endif
    public DateTime Timestamp { get; init; }
    public string ApplicationId { get; init; }
    public Guid? SagaId { get; set; }
#if UNIT_TESTING
    public StepStatus? __TestStepStatus { get; set; }
    public Type? __TestStepType { get; set; }
#endif
}