namespace Lycia.Extensions.Configurations;

public class SagaStoreOptions
{
#if NETSTANDARD2_0
	public string ApplicationId { get; set; } = string.Empty;
	public TimeSpan StepLogTtl { get; set; } = TimeSpan.FromSeconds(Constants.Ttl);
#else
    public string? ApplicationId { get; init; }
    public TimeSpan? StepLogTtl { get; init; } = TimeSpan.FromSeconds(Constants.Ttl);
#endif
}