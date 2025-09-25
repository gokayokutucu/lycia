using Lycia.Infrastructure.Retry;
using Lycia.Abstractions;
using Lycia.Middleware;
using Microsoft.Extensions.Logging;

namespace Lycia.Infrastructure.Middleware;

public interface ILoggingSagaMiddleware : ISagaMiddleware;
public sealed class LoggingMiddleware(ILogger<LoggingMiddleware> logger, ISagaContextAccessor? accessor, IRetryPolicy? retryPolicy = null) : ILoggingSagaMiddleware
{
    public Task InvokeAsync(SagaContextInvocationContext context, Func<Task> next)
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
