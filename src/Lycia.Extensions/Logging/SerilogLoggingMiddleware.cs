using Lycia.Extensions.Configurations;
using Lycia.Middleware;
using Lycia.Retry;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Contexts;
using Lycia.Saga.Abstractions.Middlewares;
using Microsoft.Extensions.Logging;
using Serilog;
using Microsoft.Extensions.Options;
using Serilog.Events;
using ILogger = Serilog.ILogger;

namespace Lycia.Extensions.Logging;

/// <summary>
/// Middleware implementation for saga handling that provides logging capabilities using Serilog.
/// </summary>
public sealed class SerilogLoggingMiddleware(
    ILogger? logger = null,
    ISagaContextAccessor? accessor = null,
    IRetryPolicy? retryPolicy = null,
    IOptions<LoggingOptions>? options = null)
    : ILoggingSagaMiddleware
{
    private readonly ILogger _logger = logger ?? Log.ForContext<SerilogLoggingMiddleware>();

    private readonly LoggingOptions _options = options?.Value ?? new LoggingOptions();

    private static LogEventLevel MapLevel(LogLevel level) => level switch
    {
        LogLevel.Trace => LogEventLevel.Verbose,
        LogLevel.Debug => LogEventLevel.Debug,
        LogLevel.Information => LogEventLevel.Information,
        LogLevel.Warning => LogEventLevel.Warning,
        LogLevel.Error => LogEventLevel.Error,
        LogLevel.Critical => LogEventLevel.Fatal,
        _ => LogEventLevel.Information
    };

    public async Task InvokeAsync(IInvocationContext context, Func<Task> next)
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

        var messageType = context.Message.GetType().Name;

        // Start
        var startTemplate = string.IsNullOrWhiteSpace(_options.StartTemplate)
            ? "Handling {MessageType}"
            : _options.StartTemplate!;
        log.Write(MapLevel(_options.MinimumLevel), startTemplate, messageType);

        try
        {
            await next();

            // Success
            var successTemplate = string.IsNullOrWhiteSpace(_options.SuccessTemplate)
                ? "Handled {MessageType} successfully"
                : _options.SuccessTemplate!;
            log.Write(MapLevel(_options.MinimumLevel), successTemplate, messageType);
        }
        catch (Exception ex)
        {
            // Error
            var errorTemplate = string.IsNullOrWhiteSpace(_options.ErrorTemplate)
                ? "Unhandled exception while handling {MessageType}"
                : _options.ErrorTemplate!;
            log.Write(MapLevel(LogLevel.Error), errorTemplate + ": {ExceptionMessage}", messageType, ex.Message);
            log.Write(LogEventLevel.Error, ex, errorTemplate, messageType);
            throw;
        }
        finally
        {
            if (retryPolicy != null)
                retryPolicy.OnRetry -= OnRetry;
        }
    }
}