using System.Reflection;
using Lycia.Messaging;
using Lycia.Messaging.Attributes;

namespace Lycia.Saga.Helpers;

public static class RoutingKeyHelper
{
    /// <summary>
    /// Returns a routing key for the given message type, based on its base class.
    /// Uses "event" or "command" as prefix if the type inherits from EventBase or CommandBase, respectively.
    /// Falls back to "message" for other types.
    /// </summary>
    public static string GetRoutingKey(Type messageType)
    {
        var attr = messageType.GetCustomAttribute<ApplicationIdAttribute>();
        if (attr == null)
            throw new InvalidOperationException($"ApplicationIdAttribute not found on {messageType.FullName}");

        // Prefix is chosen according to the base class for topology and auto-discovery.
        var prefix = messageType.IsSubclassOf(typeof(EventBase)) ? "event"
            : messageType.IsSubclassOf(typeof(CommandBase)) ? "command"
            : "message"; // No need to prefix ResponseBase/FailResponse

        // Resulting routing key format: {prefix}.{ApplicationId}.{MessageType}
        return $"{prefix}.{attr.ApplicationId}.{messageType.Name}";
    }
}