using Lycia.Retry;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Contexts;
using Lycia.Saga.Abstractions.Middlewares;
using Microsoft.Extensions.Logging;

namespace Lycia.Middleware;

/// <summary>
/// Represents middleware for logging operations in the saga middleware pipeline.
/// This middleware handles the logging of messages and sagas as they pass through the pipeline,
/// including their message and handler details as well as exception capture if errors occur.
/// </summary>
public sealed class LoggingMiddleware(
    ILogger<LoggingMiddleware> logger,
    ISagaContextAccessor? accessor,
    IRetryPolicy? retryPolicy = null) : ILoggingSagaMiddleware
{
    /// <summary>
    /// Invokes the middleware logic for the current invocation context.
    /// </summary>
    /// <param name="context">The invocation context containing information about the saga and its message.</param>
    /// <param name="next">The function delegates to invoke the next middleware in the pipeline.</param>
    /// <returns>A task representing the asynchronous operation of the middleware.</returns>
    public Task InvokeAsync(IInvocationContext context, Func<Task> next)
    {
        // Subscribe to retry events for this scope
        void OnRetryHandler(RetryContext rc)
        {
            logger.LogWarning("Retry attempt {Attempt} after {Delay}s for {Handler} due to {Exception}", rc.Attempt, rc.Delay.TotalSeconds, context.HandlerType.Name, rc.Exception.GetType().Name);
        }

        if (retryPolicy != null)
            retryPolicy.OnRetry += OnRetryHandler;
        var sagaId = context.SagaId;
        var msgId = context.Message.MessageId;
        logger.LogInformation("Handling {Message} by {Handler} [SagaId={SagaId}, MessageId={MessageId}]", context.Message.GetType().Name, context.HandlerType.Name, sagaId, msgId);
        try
        {
            return next();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception in handler {Handler} [SagaId={SagaId}, MessageId={MessageId}]", context.HandlerType.Name, sagaId, msgId);
            throw;
        }
        finally
        {
            if (retryPolicy != null)
                retryPolicy.OnRetry -= OnRetryHandler;
        }
    }
}
