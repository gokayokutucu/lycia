namespace Lycia.Extensions.Configurations;

public class EventBusOptions
{
#if NET5_0_OR_GREATER
    public string? ApplicationId { get; init; }
    public TimeSpan? MessageTTL { get; init; } = TimeSpan.FromSeconds(Constants.Ttl);
    //Dead Letter Queue (DLQ) TTL must be same or no longer than MessageTTL
    public TimeSpan? DeadLetterQueueMessageTTL { get; init; } = TimeSpan.FromSeconds(Constants.Ttl); 
#else
    public string ApplicationId { get; set; } = string.Empty;
    public TimeSpan MessageTTL { get; set; } = TimeSpan.FromSeconds(Constants.Ttl);
    //Dead Letter Queue (DLQ) TTL must be same or no longer than MessageTTL
    public TimeSpan DeadLetterQueueMessageTTL { get; set; } = TimeSpan.FromSeconds(Constants.Ttl);
#endif
}