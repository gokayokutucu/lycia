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
/// The <see cref="ActivitySagaMiddleware"/> is responsible for creating and managing diagnostic
/// activities for individual saga steps. It utilizes the <see cref="LyciaActivitySourceHolder"/> to
/// generate activities, setting relevant contextual information such as saga ID, message ID,
/// correlation ID, handler, and application ID as diagnostic tags.
/// </remarks>
public sealed class ActivitySagaMiddleware(
    LyciaActivitySourceHolder sourceHolder,
    ISagaContextAccessor accessor,
    ILogger<ActivitySagaMiddleware> logger)
    : ISagaMiddleware
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
        var activitySource = sourceHolder.Source;
        using var activity = activitySource.StartActivity("Lycia.Saga.Step", ActivityKind.Internal);

        if (activity is not null)
        {
            activity.SetTag("lycia.saga_id", context.SagaId.ToString());
            activity.SetTag("lycia.message_id", context.Message.MessageId.ToString());
            activity.SetTag("lycia.correlation_id", context.Message.CorrelationId.ToString());
            activity.SetTag("lycia.handler", context.HandlerType.FullName);
            activity.SetTag("lycia.application_id", context.ApplicationId);
        }

        try
        {
            await next();
            activity?.SetTag("lycia.step_status", "Completed");
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("exception.type", ex.GetType().FullName);
            activity?.SetTag("exception.message", ex.Message);
            activity?.SetTag("exception.stacktrace", ex.StackTrace);
            throw;
        }
    }
}