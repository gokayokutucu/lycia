using Lycia.Retry;
using Lycia.Saga.Abstractions.Contexts;
using Lycia.Saga.Abstractions.Middlewares;

namespace Lycia.Middleware;

/// <summary>Identifies the saga middleware responsible for applying retry policy.</summary>
public interface IRetrySagaMiddleware : ISagaMiddleware;
/// <summary>Executes the remainder of a saga middleware pipeline through an <see cref="IRetryPolicy"/>.</summary>
public sealed class RetryMiddleware(IRetryPolicy retryPolicy) : IRetrySagaMiddleware
{
    /// <inheritdoc />
    public Task InvokeAsync(IInvocationContext context, Func<Task> next)
    {
        // Do not hardcode any retry logic here; delegate to the policy abstraction
        return retryPolicy.ExecuteAsync(next, context.CancellationToken).AsTask();
    }
}
