using System.Diagnostics;
using Lycia.Observability;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Contexts;
using Lycia.Saga.Abstractions.Middlewares;
using Microsoft.Extensions.Logging;

namespace Lycia.Middleware;

/// <summary>
/// Middleware for handling activity tracking within a saga execution context.
/// </summary>
/// <remarks>
/// The <see cref="ActivityTracingMiddleware"/> is responsible for creating and managing diagnostic
/// activities for individual saga steps. It utilizes the <see cref="LyciaActivitySourceHolder"/> to
/// generate activities, setting relevant contextual information such as saga ID, message ID,
/// correlation ID, handler, and application ID as diagnostic tags.
/// </remarks>
public sealed class ActivityTracingMiddleware(
    LyciaActivitySourceHolder sourceHolder,
    ISagaContextAccessor accessor,
    ILogger<ActivityTracingMiddleware> logger)
    : ITracingSagaMiddleware
{
    /// <summary>
    /// Executes the middleware logic, including starting and stopping Activity for tracing
    /// and invoking the next delegate in the middleware pipeline.
    /// </summary>
    /// <param name="context">
    /// The invocation context containing information about the current saga execution,
    /// such as saga ID, message details, and handler information.
    /// </param>
    /// <param name="next">
    /// A delegate representing the next step in the middleware pipeline.
    /// This function should be called to pass control to the subsequent middleware or handler.
    /// </param>
    /// <returns>
    /// A Task that represents the asynchronous operation of the middleware execution.
    /// </returns>
    public async Task InvokeAsync(IInvocationContext context, Func<Task> next)
    {
        // span name: "Saga.{HandlerName}"
        var handlerName = context.HandlerType?.Name ?? "UnknownHandler";
        var activitySource = sourceHolder.Source;

        // Reuse Activity created by the listener if available; otherwise create a new one as fallback
        Activity? createdActivity = null;
        var current = Activity.Current;
        if (current is null)
        {
            createdActivity = activitySource.StartActivity($"Saga.{handlerName}", ActivityKind.Internal);
            current = createdActivity;
        }

        // If no Activity at all, tracing is disabled; just continue.
        if (current is not null)
        {
            // Basic saga/message tags
            current.SetTag("lycia.saga.id", context.SagaId.ToString());
            current.SetTag("lycia.message.id", context.Message.MessageId.ToString());
            current.SetTag("lycia.handler", context.HandlerType?.FullName);
            current.SetTag("lycia.correlation.id", context.Message.CorrelationId.ToString());
            
            // If the message exposes ApplicationId, propagate it as well
            if (!string.IsNullOrWhiteSpace(context.Message.ApplicationId))
            {
                current.SetTag("lycia.application.id", context.Message.ApplicationId);
            }
        }

        try
        {
            await next().ConfigureAwait(false);
            current?.SetTag("lycia.saga.step.status", "Completed");
        }
        catch (Exception ex)
        {
            // Record exception on the span
            current?.SetStatus(ActivityStatusCode.Error, ex.Message);
            current?.SetTag("exception.type", ex.GetType().FullName);
            current?.SetTag("exception.message", ex.Message);
            current?.SetTag("exception.stacktrace", ex.StackTrace);

            logger.LogError(ex,
                "Unhandled exception in saga handler {Handler} [SagaId={SagaId}, MessageId={MessageId}]",
                handlerName,
                context.SagaId,
                context.Message.MessageId);

            throw;
        }
        finally
        {
            // Dispose only if we created the activity here
            createdActivity?.Dispose();
        }
    }
}