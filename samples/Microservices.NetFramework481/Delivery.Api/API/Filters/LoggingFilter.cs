using System;
using System.Diagnostics;
using System.Web.Http.Controllers;
using System.Web.Http.Filters;
using Serilog;

namespace Sample.Delivery.NetFramework481.API.Filters;

/// <summary>
/// Action filter for logging HTTP requests.
/// </summary>
public sealed class LoggingFilter : ActionFilterAttribute
{
    /// <summary>
    /// Logs before action execution.
    /// </summary>
    public override void OnActionExecuting(HttpActionContext context)
    {
        context.Request.Properties["Stopwatch"] = Stopwatch.StartNew();

        Log.Information(
            "HTTP {Method} {Path} started by {User}",
            context.Request.Method,
            context.Request.RequestUri?.PathAndQuery,
            context.RequestContext.Principal?.Identity?.Name ?? "Anonymous");
    }

    /// <summary>
    /// Logs after action execution.
    /// </summary>
    public override void OnActionExecuted(HttpActionExecutedContext context)
    {
        if (context.Request.Properties.TryGetValue("Stopwatch", out var stopwatchObj) && stopwatchObj is Stopwatch stopwatch)
        {
            stopwatch.Stop();

            Log.Information(
                "HTTP {Method} {Path} completed with {StatusCode} in {ElapsedMilliseconds}ms",
                context.Request.Method,
                context.Request.RequestUri?.PathAndQuery,
                (int)(context.Response?.StatusCode ?? System.Net.HttpStatusCode.OK),
                stopwatch.ElapsedMilliseconds);
        }
    }
}
