using Lycia.Messaging.Enums;

namespace Lycia.Messaging;

public interface IMessage
{
    Guid MessageId { get; }
    DateTime Timestamp { get; }
    string ApplicationId { get; } 
#if UNIT_TESTING
    StepStatus? __TestStepStatus { get; set; }
    Type? __TestStepType { get; set; }
#endif
}

public interface ICommand : IMessage {}
public interface IEvent : IMessage {}