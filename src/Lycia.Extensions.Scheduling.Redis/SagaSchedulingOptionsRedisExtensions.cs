// TargetFramework: netstandard2.0
using Lycia.Extensions.Configurations;

namespace Lycia.Extensions.Scheduling.Redis
{
    /// <summary>
    /// Redis-specific decorator for SagaSchedulingOptions.
    /// Kept outside of the core to avoid backend leakage into the options type.
    /// </summary>
    public static class SagaSchedulingOptionsRedisExtensions
    {
        public static SagaSchedulingOptions UseRedis(this SagaSchedulingOptions options, string? configSection = "Lycia:Scheduling")
        {
            return options.UseProvider("Redis", configSection);
        }
    }
}