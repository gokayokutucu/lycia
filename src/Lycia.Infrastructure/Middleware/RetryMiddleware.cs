using Lycia.Infrastructure.Retry;
using Lycia.Saga;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Middleware;

namespace Lycia.Infrastructure.Middleware;

public interface IRetrySagaMiddleware : ISagaMiddleware;
public sealed class RetryMiddleware(IRetryPolicy retryPolicy) : IRetrySagaMiddleware
{
    public Task InvokeAsync(SagaContextInvocationContext context, Func<Task> next)
    {
        // Do not hardcode any retry logic here; delegate to the policy abstraction
        return retryPolicy.ExecuteAsync(next, context.CancellationToken);
    }
}
