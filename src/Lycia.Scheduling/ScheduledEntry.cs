// TargetFramework: netstandard2.0

namespace Lycia.Scheduling;

public sealed class ScheduledEntry
{
    public Guid Id { get; set; }
    public DateTimeOffset DueTime { get; set; }
    public byte[]? Payload { get; set; }
    public Type? MessageType { get; set; }
    public Dictionary<string, object> Headers { get; set; } = new();
    public Guid CorrelationId { get; set; }
    public Guid MessageId { get; set; }
}