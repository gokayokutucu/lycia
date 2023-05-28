using System.Reflection;

namespace Lycia.Dapr.Messages.Abstractions;

public interface IEventBus
{
    Dictionary<string, IEventHandler> Topics { get; }
    void Subscribe(Assembly handler, string? prefix = null, string? suffix = null);
}