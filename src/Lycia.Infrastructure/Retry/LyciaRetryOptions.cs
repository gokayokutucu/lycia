namespace Lycia.Infrastructure.Retry;

// TargetFramework: netstandard2.0
public sealed class LyciaRetryOptions
{
    public int MaxRetryAttempts { get; set; } = 3;
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan? MaxDelay { get; set; }
    public bool UseJitter { get; set; } = true;
    public List<Type> HandledExceptions { get; set; } = new List<Type>();
}