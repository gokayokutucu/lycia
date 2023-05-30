using System.Collections.Concurrent;
using System.Reflection;
using Lycia.Dapr.Messages.Abstractions;

namespace Lycia.Dapr.EventBus.Abstractions;

public interface IEventBus
{
    ConcurrentDictionary<string, IEventHandler> Topics { get; }

    void Subscribe(Assembly handler, string? prefix = null, string? suffix = null);
}