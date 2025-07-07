using System.Reflection;

namespace Lycia.Messaging.Utility;

public static class EventMetadata
{
    public static string ApplicationId { get; set; } = Assembly.GetEntryAssembly()?.GetName().Name ?? "Unknown";
}