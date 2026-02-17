using Microsoft.Extensions.Options;
using Polly.Retry;
using Lycia.Retry;
using Lycia.Saga.Exceptions;
using Polly;

namespace Lycia.Tests.Retry
{
    public class PollyRetryPolicyTests
    {
        [Fact]
        public async Task Retries_TransientSagaException_UpTo_MaxAttempts_Then_Succeeds()
        {
            // Arrange
            var opts = Options.Create(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromMilliseconds(1),
                UseJitter = false
            });

            var policy = new PollyRetryPolicy(opts);

            int attempts = 0;

            // Act
            await policy.ExecuteAsync(Flaky, CancellationToken.None);

            // Assert
            Assert.Equal(3, attempts); // 2 retry + 1 başarı
            return;

            Task Flaky()
            {
                attempts++;
                if (attempts < 3) throw new TransientSagaException("flaky");
                return Task.CompletedTask;
            }
        }

        [Fact]
        public async Task Does_Not_Retry_For_Unhandled_Exception()
        {
            var opts = Options.Create(new RetryStrategyOptions
            {
                MaxRetryAttempts = 5,
                Delay = TimeSpan.FromMilliseconds(1),
                UseJitter = false
            });

            var policy = new PollyRetryPolicy(opts);

            int attempts = 0;
            Func<Task> bad = () =>
            {
                attempts++;
                throw new InvalidOperationException("no retry");
            };

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => policy.ExecuteAsync(bad).AsTask());
            Assert.Equal(1, attempts);
        }

        [Fact]
        public async Task OnRetry_Event_Is_Raised_On_Each_Attempt()
        {
            var opts = Options.Create(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromMilliseconds(1),
                UseJitter = false
            });

            var policy = new PollyRetryPolicy(opts);

            int onRetryCalls = 0;
            policy.OnRetry += ctx =>
            {
                onRetryCalls++;
                Assert.IsType<TransientSagaException>(ctx.Exception);
            };

            int attempts = 0;

            await policy.ExecuteAsync(Flaky, CancellationToken.None);
            
            Assert.Equal(2, onRetryCalls);
            return;

            Task Flaky()
            {
                attempts++;
                return attempts < 3 ? throw new TransientSagaException("will retry") : Task.CompletedTask;
            }
        }
    }
}
