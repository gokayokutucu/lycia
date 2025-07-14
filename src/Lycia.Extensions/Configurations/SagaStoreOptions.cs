namespace Lycia.Extensions.Configurations;

public class SagaStoreOptions
{
    public string? ApplicationId { get; set; }
    public TimeSpan? StepLogTtl { get; set; }= TimeSpan.FromSeconds(Constants.Ttl);
}