using Microsoft.Owin;
using Newtonsoft.Json;
using Serilog;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Sample.Order.NetFramework481.API.Middleware;

/// <summary>
/// Global exception handling middleware for Order.Api
/// </summary>
public class GlobalExceptionMiddleware(OwinMiddleware next) : OwinMiddleware(next)
{
    private static readonly ILogger Logger = Log.ForContext<GlobalExceptionMiddleware>();

    public override async Task Invoke(IOwinContext context)
    {
        var requestId = Guid.NewGuid().ToString("N");
        context.Response.Headers.Add("X-Request-Id", [requestId]);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            await Next.Invoke(context);
        }
        catch (InvalidOperationException ex)
        {
            stopwatch.Stop();
            Logger.Error(
                ex,
                "🔴 Order.Api Business Logic Error | RequestId: {RequestId} | Path: {Path} | Duration: {Duration}ms | Message: {Message}",
                requestId,
                context.Request.Path.Value,
                stopwatch.ElapsedMilliseconds,
                ex.Message
            );

            context.Response.StatusCode = 400;
            context.Response.ContentType = "application/json";

            await context.Response.WriteAsync(JsonConvert.SerializeObject(new
            {
                error = "Business Logic Error",
                service = "Order.Api",
                requestId,
                message = ex.Message,
                timestamp = DateTime.UtcNow
            }));
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Logger.Error(
                ex,
                "🔴 Order.Api Unhandled Exception | RequestId: {RequestId} | Path: {Path} | Method: {Method} | Duration: {Duration}ms | ExceptionType: {ExceptionType}",
                requestId,
                context.Request.Path.Value,
                context.Request.Method,
                stopwatch.ElapsedMilliseconds,
                ex.GetType().Name
            );

            context.Response.StatusCode = 500;
            context.Response.ContentType = "application/json";

            await context.Response.WriteAsync(JsonConvert.SerializeObject(new
            {
                error = "Internal Server Error",
                service = "Order.Api",
                requestId,
                message = ex.Message,
                timestamp = DateTime.UtcNow
            }));
        }
    }
}
