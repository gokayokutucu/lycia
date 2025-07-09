namespace Lycia.Extensions.Configurations;

public class EventBusOptions
{
    public string? ApplicationId { get; init; }
    public TimeSpan? MessageTTL { get; init; } = TimeSpan.FromSeconds(Constants.Ttl);
    //Dead Letter Queue (DLQ) TTL must be same or no longer than MessageTTL
    public TimeSpan? DeadLetterQueueMessageTTL { get; init; } = TimeSpan.FromSeconds(Constants.Ttl);
}