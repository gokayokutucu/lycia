namespace Lycia.Saga.Abstractions.Messaging;

public interface IFailedEventBase : IEvent
{
    public string Reason { get; }
}