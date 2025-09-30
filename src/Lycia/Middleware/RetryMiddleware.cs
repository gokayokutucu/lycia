using Lycia.Retry;
using Lycia.Saga.Abstractions.Contexts;
using Lycia.Saga.Abstractions.Middlewares;

namespace Lycia.Middleware;

public interface IRetrySagaMiddleware : ISagaMiddleware;
public sealed class RetryMiddleware(IRetryPolicy retryPolicy) : IRetrySagaMiddleware
{
    public Task InvokeAsync(IInvocationContext context, Func<Task> next)
    {
        // Do not hardcode any retry logic here; delegate to the policy abstraction
        return retryPolicy.ExecuteAsync(next, context.CancellationToken).AsTask();
    }
}
