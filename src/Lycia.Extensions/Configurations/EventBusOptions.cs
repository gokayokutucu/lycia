namespace Lycia.Extensions.Configurations;

public class EventBusOptions
{
    public string? ApplicationId { get; set; }
    public TimeSpan? MessageTTL { get; set; } = TimeSpan.FromSeconds(Constants.Ttl);
    //Dead Letter Queue (DLQ) TTL must be same or no longer than MessageTTL
    public TimeSpan? DeadLetterQueueMessageTTL { get; set; } = TimeSpan.FromSeconds(Constants.Ttl);
}