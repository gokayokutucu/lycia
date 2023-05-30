using System.Reflection;
using Lycia.Dapr.Messages.Abstractions;

namespace Lycia.Dapr.EventBus.Abstractions;

public interface IEventBus
{
    Dictionary<string, IEventHandler> Topics { get; }

    void Subscribe(Assembly handler, string? prefix = null, string? suffix = null);
}

// Bir eventin birden fazla dinleyicisi olabilir
// Bu yüzden List<EventHandler> olmalıdır