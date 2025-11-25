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

        using var activity = activitySource.StartActivity(
            $"Saga.{handlerName}",
            ActivityKind.Internal);

        // If Activity is null, tracing is disabled; just continue.
        if (activity is not null)
        {
            // Basic saga/message tags
            activity.SetTag("lycia.saga.id", context.SagaId.ToString());
            activity.SetTag("lycia.message.id", context.Message.MessageId.ToString());
            activity.SetTag("lycia.handler", context.HandlerType?.FullName);
            activity.SetTag("lycia.correlation.id", context.Message.CorrelationId.ToString());
            
            // If the message exposes ApplicationId, propagate it as well
            if (!string.IsNullOrWhiteSpace(context.Message.ApplicationId))
            {
                activity.SetTag("lycia.application.id", context.Message.ApplicationId);
            }
        }

        try
        {
            await next().ConfigureAwait(false);
            activity?.SetTag("lycia.saga.step.status", "Completed");
        }
        catch (Exception ex)
        {
            // Record exception on the span
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("exception.type", ex.GetType().FullName);
            activity?.SetTag("exception.message", ex.Message);
            activity?.SetTag("exception.stacktrace", ex.StackTrace);

            logger.LogError(ex,
                "Unhandled exception in saga handler {Handler} [SagaId={SagaId}, MessageId={MessageId}]",
                handlerName,
                context.SagaId,
                context.Message.MessageId);

            throw;
        }
    }
}