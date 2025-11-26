using Lycia.Contexts;
using Lycia.Middleware;
using Lycia.Retry;
using Lycia.Saga.Abstractions.Middlewares;
using Lycia.Saga.Exceptions;
using Polly;
using Microsoft.Extensions.DependencyInjection;
using Polly.Retry;
using IRetryPolicy = Lycia.Retry.IRetryPolicy;


namespace Lycia.Tests.Retry
{
    public class RetryMiddlewarePipelineTests
    {
        [Fact]
        public async Task Pipeline_Retries_And_Succeeds_On_TransientSagaException()
        {
            // Arrange DI
            var services = new ServiceCollection();
            services.AddLogging(); 

            // Retry options
            services.AddOptions<RetryStrategyOptions>().Configure(o =>
            {
                o.MaxRetryAttempts = 3;
                o.Delay = TimeSpan.FromMilliseconds(1);
                o.BackoffType = DelayBackoffType.Exponential;
                o.UseJitter = false;
                o.ShouldHandle = new PredicateBuilder().Handle<TransientSagaException>();
            });

            services.AddSingleton<IRetryPolicy, PollyRetryPolicy>();
            services.AddSingleton<ISagaMiddleware, RetryMiddleware>();

            // (Optional) Logging middleware
            // services.AddSingleton<ISagaMiddleware, LoggingMiddleware>();

            var sp = services.BuildServiceProvider();
            var ordered = new[] { typeof(RetryMiddleware) };
            var pipeline = new SagaMiddlewarePipeline(ordered, sp);

            int attempts = 0;

            var ctx = new SagaContextInvocationContext
            {
                CancellationToken = CancellationToken.None
            };

            // Act
            await pipeline.InvokeAsync(ctx, Flaky);

            // Assert
            Assert.Equal(3, attempts);
            return;

            Task Flaky()
            {
                attempts++;
                return attempts < 3 ? throw new TransientSagaException("flaky") : Task.CompletedTask;
            }
        }

        [Fact]
        public async Task Pipeline_Does_Not_Retry_For_Unhandled_Exception()
        {
            var services = new ServiceCollection();
            services.AddLogging();

            services.AddOptions<RetryStrategyOptions>().Configure(o =>
            {
                o.MaxRetryAttempts = 5;
                o.Delay = TimeSpan.FromMilliseconds(1);
                o.UseJitter = false;
                o.ShouldHandle = new PredicateBuilder().Handle<TransientSagaException>();
            });

            services.AddSingleton<IRetryPolicy, PollyRetryPolicy>();
            services.AddSingleton<ISagaMiddleware, RetryMiddleware>();

            var sp = services.BuildServiceProvider();
            var pipeline = new SagaMiddlewarePipeline([typeof(RetryMiddleware)], sp);

            int attempts = 0;
            Func<Task> bad = () =>
            {
                attempts++;
                throw new InvalidOperationException("no retry");
            };

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                pipeline.InvokeAsync(new SagaContextInvocationContext(), bad));

            Assert.Equal(1, attempts);
        }
    }
}