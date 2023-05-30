using System.Reflection;
using Lycia.Dapr.Messages.Abstractions;

namespace Lycia.Dapr.EventBus.Abstractions;

public interface IEventBus
{
    Dictionary<string, IEventHandler> Topics { get; }

    void Subscribe<T>(Assembly handler, string? prefix = null, string? suffix = null) where T : IEventHandler;
}