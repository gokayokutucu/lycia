using Lycia.Middleware;
using Lycia.Retry;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Contexts;
using Serilog;

namespace Lycia.Extensions.Logging;

/// <summary>
/// Middleware implementation for saga handling that provides logging capabilities using Serilog.
/// </summary>
public sealed class SerilogLoggingMiddleware(
    ILogger? logger = null,
    ISagaContextAccessor? accessor = null,
    IRetryPolicy? retryPolicy = null)
    : ILoggingSagaMiddleware
{
    private readonly ILogger _logger = logger ?? Log.ForContext<SerilogLoggingMiddleware>();

    public Task InvokeAsync(IInvocationContext context, Func<Task> next)
    {
        void OnRetry(RetryContext rc)
        {
            _logger
                .ForContext("SagaId", context.SagaId, false)
                .ForContext("MessageId", context.Message.MessageId, false)
                .ForContext("CorrelationId", context.Message.CorrelationId, false)
                .ForContext("Handler", context.HandlerType.Name, false)
                .Information("Retry attempt {Attempt} after {Delay} due to {ExceptionType}",
                    rc.Attempt, rc.Delay, rc.Exception.GetType().Name);
        }

        if (retryPolicy != null)
            retryPolicy.OnRetry += OnRetry;

        var log = _logger
            .ForContext("SagaId", context.SagaId, false)
            .ForContext("MessageId", context.Message.MessageId, false)
            .ForContext("CorrelationId", context.Message.CorrelationId, false)
            .ForContext("Handler", context.HandlerType.Name, false);

        log.Information("Handling {MessageType}", context.Message.GetType().Name);
        try
        {
            return next();
        }
        catch (Exception ex)
        {
            log.Error(ex, "Unhandled exception while handling {MessageType}", context.Message.GetType().Name);
            throw;
        }
        finally
        {
            if (retryPolicy != null)
                retryPolicy.OnRetry -= OnRetry;
        }
    }
}