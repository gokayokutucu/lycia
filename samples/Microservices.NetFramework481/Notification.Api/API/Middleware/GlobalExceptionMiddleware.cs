using Microsoft.Owin;
using Newtonsoft.Json;
using Serilog;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Sample.Notification.NetFramework481.API.Middleware
{
    public class GlobalExceptionMiddleware : OwinMiddleware
    {
        private static readonly ILogger Logger = Log.ForContext<GlobalExceptionMiddleware>();

        public GlobalExceptionMiddleware(OwinMiddleware next) : base(next)
        {
        }

        public override async Task Invoke(IOwinContext context)
        {
            var requestId = Guid.NewGuid().ToString("N");
            context.Response.Headers.Add("X-Request-Id", new[] { requestId });
            var stopwatch = Stopwatch.StartNew();

            try
            {
                await Next.Invoke(context);
            }
            catch (InvalidOperationException ex)
            {
                stopwatch.Stop();
                Logger.Error(ex, "🔴 Notification.Api Business Logic Error | RequestId: {RequestId} | Path: {Path} | Duration: {Duration}ms", requestId, context.Request.Path.Value, stopwatch.ElapsedMilliseconds);
                context.Response.StatusCode = 400;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonConvert.SerializeObject(new { error = "Business Logic Error", service = "Notification.Api", requestId, message = ex.Message, timestamp = DateTime.UtcNow }));
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                Logger.Error(ex, "🔴 Notification.Api Unhandled Exception | RequestId: {RequestId} | Path: {Path} | Duration: {Duration}ms", requestId, context.Request.Path.Value, stopwatch.ElapsedMilliseconds);
                context.Response.StatusCode = 500;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonConvert.SerializeObject(new { error = "Internal Server Error", service = "Notification.Api", requestId, message = ex.Message, timestamp = DateTime.UtcNow }));
            }
        }
    }
}
