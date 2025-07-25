namespace Lycia.Extensions.Configurations;

public class SagaStoreOptions
{
#if NETSTANDARD2_0
    public string? ApplicationId { get; set; }
    public TimeSpan? StepLogTtl { get; set; }= TimeSpan.FromSeconds(Constants.Ttl);
    public int LogMaxRetryCount { get; set; } = 5;
#else
    public string? ApplicationId { get; init; }
    public TimeSpan? StepLogTtl { get; init; } = TimeSpan.FromSeconds(Constants.Ttl);
    public int LogMaxRetryCount { get; init; } = 5;
#endif
}