using Lycia.Dapr.Messages.Abstractions;

namespace Lycia.Dapr.Messages;

public class DaprEventBus : IEventBus
{
    ///<inheritdoc/>
    public Dictionary<string, List<IEventHandler>> Topics { get; } = new();

    public DaprEventBus()
    {
        //Topics.Add("OrderCreatedCommand", new List<IEventHandler> { new OrderCreatedEventHandler() });
    }
}