using Lycia.Saga.Abstractions.Messaging;

namespace Lycia.Saga.Messaging;

public abstract class FailedEventBase(string reason) : EventBase, IFailedEventBase
{
    public string Reason { get; private set; } = reason;
}