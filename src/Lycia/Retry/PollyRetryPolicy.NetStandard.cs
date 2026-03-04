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

            var policyBuilder = Policy.Handle<Exception>(ex =>
            {
                if (src?.ShouldHandle != null)
                    return src.ShouldHandle.ShouldHandle(ex);
                return ex is TransientSagaException or TimeoutException;
            });

            _policy = policyBuilder
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
        public DelayBackoffType BackoffType { get; set; } = DelayBackoffType.Exponential;
        public PredicateBuilder? ShouldHandle { get; set; }
    }

    // Mirror Polly v8 DelayBackoffType enum
    public enum DelayBackoffType
    {
        Constant,
        Linear,
        Exponential
    }

    // Mirror Polly v8 PredicateBuilder (simplified)
    public class PredicateBuilder
    {
        private readonly List<Type> _exceptionTypes = new();

        public PredicateBuilder Handle<TException>() where TException : Exception
        {
            _exceptionTypes.Add(typeof(TException));
            return this;
        }

        internal bool ShouldHandle(Exception ex)
        {
            return _exceptionTypes.Any(t => t.IsInstanceOfType(ex));
        }
    }
}
#endif
