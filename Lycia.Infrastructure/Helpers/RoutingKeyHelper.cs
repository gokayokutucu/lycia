using System.Reflection;
using Lycia.Messaging.Attributes;

namespace Lycia.Infrastructure.Helpers;

public static class RoutingKeyHelper
{
    public static string GetRoutingKey(Type messageType)
    {
        var attr = messageType.GetCustomAttribute<ApplicationIdAttribute>();
        if (attr == null)
            throw new InvalidOperationException($"ApplicationIdAttribute not found on {messageType.FullName}");
        return $"{attr.ApplicationId}.{messageType.Name}";
    }
}