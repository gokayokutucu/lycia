namespace Lycia.Extensions.Configurations;

public class SagaStoreOptions
{
#if NET5_0_OR_GREATER
	public string? ApplicationId { get; init; }
	public TimeSpan? StepLogTtl { get; init; } = TimeSpan.FromSeconds(Constants.Ttl); 
#else
	public string ApplicationId { get; set; } = string.Empty;
	public TimeSpan StepLogTtl { get; set; } = TimeSpan.FromSeconds(Constants.Ttl);
#endif
}