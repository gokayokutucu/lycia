using Lycia.Saga.Exceptions;
using Microsoft.Extensions.Options;
using Polly;

#if NETSTANDARD2_0
namespace Lycia.Retry
{
    public class PollyRetryPolicy : IRetryPolicy
    {
        // Polly v7 API (for .NET Standard 2.0)
        private readonly IAsyncPolicy _policy;
        public event Action<RetryContext>? OnRetry;

        public PollyRetryPolicy(IOptions<RetryStrategyOptions>? options)
        {
            var src = options?.Value;
            var maxRetryAttempts = src?.MaxRetryAttempts ?? 3;

            _policy = Policy
                .Handle<TransientSagaException>()
                .Or<TimeoutException>()
                .WaitAndRetryAsync(
                    maxRetryAttempts,
                    retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                    (exception, timeSpan, retryCount, context) =>
                    {
                        var ctx = new RetryContext(exception, (int)retryCount, timeSpan);
                        OnRetry?.Invoke(ctx);
                    });
        }

        public async ValueTask ExecuteAsync(Func<Task> action, CancellationToken cancellationToken = default)
        {
            await _policy.ExecuteAsync(async (ct) => await action(), cancellationToken);
        }

        public bool ShouldRetry(Exception? exception, int currentRetryCount)
        {
            return exception is TransientSagaException or TimeoutException && currentRetryCount < 3;
        }

        public TimeSpan GetDelay(Exception? exception, int currentRetryCount)
        {
            return exception switch
            {
                TimeoutException => TimeSpan.FromSeconds(1),
                TransientSagaException => TimeSpan.FromSeconds(3),
                _ => TimeSpan.FromSeconds(Math.Pow(2, currentRetryCount))
            };
        }
    }

    // .NET Standard 2.0 compatible RetryStrategyOptions (mirrors Polly v8 structure)
    public class RetryStrategyOptions
    {
        public int MaxRetryAttempts { get; set; } = 3;
        public TimeSpan Delay { get; set; } = TimeSpan.FromSeconds(1);
        public TimeSpan? MaxDelay { get; set; }
        public bool UseJitter { get; set; } = true;
    }
}
#endif
