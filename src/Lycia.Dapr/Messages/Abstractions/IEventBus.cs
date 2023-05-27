namespace Lycia.Dapr.Messages.Abstractions;

public interface IEventBus
{
    Dictionary<string, List<IEventHandler>> Topics { get; }
}