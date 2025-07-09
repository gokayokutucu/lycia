namespace Lycia.Extensions.Configurations;

public class SagaStoreOptions
{
    public string? ApplicationId { get; init; }
    public TimeSpan? StepLogTtl { get; init; }= TimeSpan.FromSeconds(Constants.Ttl);
}