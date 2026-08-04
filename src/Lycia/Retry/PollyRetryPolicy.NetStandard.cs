using Lycia.Saga.Exceptions;
using Microsoft.Extensions.Options;
using Polly;

#if NETSTANDARD2_0
namespace Lycia.Retry
{
    /// <summary>Implements Lycia retry semantics with the Polly 7 API available on .NET Standard 2.0.</summary>
    public class PollyRetryPolicy : IRetryPolicy
    {
        // Polly v7 API (for .NET Standard 2.0)
        private readonly IAsyncPolicy _policy;
        /// <inheritdoc />
        public event Action<RetryContext>? OnRetry;

        /// <summary>Creates a retry policy from optional strategy settings.</summary>
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

        /// <inheritdoc />
        public async ValueTask ExecuteAsync(Func<Task> action, CancellationToken cancellationToken = default)
        {
            await _policy.ExecuteAsync(async (ct) => await action(), cancellationToken);
        }

        /// <inheritdoc />
        public bool ShouldRetry(Exception? exception, int currentRetryCount)
        {
            return exception is TransientSagaException or TimeoutException && currentRetryCount < 3;
        }

        /// <inheritdoc />
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

    /// <summary>Defines retry options compatible with the Polly 8 option shape on modern targets.</summary>
    public class RetryStrategyOptions
    {
        /// <summary>Gets or sets the maximum retry attempts after the initial execution.</summary>
        public int MaxRetryAttempts { get; set; } = 3;
        /// <summary>Gets or sets the base delay used by the backoff strategy.</summary>
        public TimeSpan Delay { get; set; } = TimeSpan.FromSeconds(1);
        /// <summary>Gets or sets an optional upper bound for retry delays.</summary>
        public TimeSpan? MaxDelay { get; set; }
        /// <summary>Gets or sets whether random jitter is applied to retry delays.</summary>
        public bool UseJitter { get; set; } = true;
        /// <summary>Gets or sets the delay growth strategy.</summary>
        public DelayBackoffType BackoffType { get; set; } = DelayBackoffType.Exponential;
        /// <summary>Gets or sets the exception predicate used to select retryable failures.</summary>
        public PredicateBuilder? ShouldHandle { get; set; }
    }

    /// <summary>Specifies how retry delays increase between attempts.</summary>
    public enum DelayBackoffType
    {
        /// <summary>Uses the same delay for every retry.</summary>
        Constant,
        /// <summary>Increases the delay linearly.</summary>
        Linear,
        /// <summary>Increases the delay exponentially.</summary>
        Exponential
    }

    /// <summary>Builds an exception-type predicate for .NET Standard retry configuration.</summary>
    public class PredicateBuilder
    {
        private readonly List<Type> _exceptionTypes = new();

        /// <summary>Adds an exception type that should be handled by the retry policy.</summary>
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
