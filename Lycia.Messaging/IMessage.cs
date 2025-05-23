namespace Lycia.Messaging;

public interface IMessage
{
    Guid MessageId { get; }
    DateTime Timestamp { get; }
    string ApplicationId { get; } 
}

public interface ICommand : IMessage {}
public interface IEvent : IMessage {}