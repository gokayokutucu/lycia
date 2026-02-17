using Serilog;
using System.Diagnostics;
using System.Text;

namespace Gateway.Api.Middleware;

/// <summary>
/// Middleware for logging all incoming requests and outgoing responses in Gateway
/// </summary>
public class RequestResponseLoggingMiddleware
{
    private readonly RequestDelegate _next;

    public RequestResponseLoggingMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var requestId = Guid.NewGuid().ToString("N");
        context.Items["RequestId"] = requestId;
        context.Response.Headers.Add("X-Request-Id", requestId);

        var stopwatch = Stopwatch.StartNew();

        // Read request body
        context.Request.EnableBuffering();
        var requestBody = await ReadRequestBodyAsync(context.Request);

        // Read request headers
        var requestHeaders = context.Request.Headers
            .Where(h => !h.Key.StartsWith("X-Gateway") && h.Key != "Authorization") // Redact sensitive headers
            .ToDictionary(h => h.Key, h => string.Join(", ", h.Value));

        // Log incoming request with headers and body
        Log.Information(
            "🔵 Gateway Request | RequestId: {RequestId} | Method: {Method} | Path: {Path} | Query: {Query} | ClientIP: {ClientIP} | Headers: {@Headers} | Body: {Body}",
            requestId,
            context.Request.Method,
            context.Request.Path,
            context.Request.QueryString.ToString(),
            context.Connection.RemoteIpAddress?.ToString(),
            requestHeaders,
            requestBody
        );

        // Capture response
        var originalResponseBody = context.Response.Body;
        using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        try
        {
            await _next(context);

            stopwatch.Stop();

            // Read response body
            responseBody.Seek(0, SeekOrigin.Begin);
            var responseBodyText = await new StreamReader(responseBody).ReadToEndAsync();
            responseBody.Seek(0, SeekOrigin.Begin);

            // Read response headers
            var responseHeaders = context.Response.Headers
                .Where(h => !h.Key.StartsWith("X-Gateway"))
                .ToDictionary(h => h.Key, h => string.Join(", ", h.Value));

            // Copy response body back
            await responseBody.CopyToAsync(originalResponseBody);

            // Log outgoing response with headers and body
            var statusCode = context.Response.StatusCode;
            if (statusCode >= 200 && statusCode < 300)
            {
                Log.Information(
                    "🟢 Gateway Response | RequestId: {RequestId} | StatusCode: {StatusCode} | Duration: {Duration}ms | Headers: {@Headers} | Body: {Body}",
                    requestId,
                    statusCode,
                    stopwatch.ElapsedMilliseconds,
                    responseHeaders,
                    TruncateBody(responseBodyText, 2000)
                );
            }
            else if (statusCode >= 400)
            {
                Log.Warning(
                    "🟡 Gateway Response Error | RequestId: {RequestId} | StatusCode: {StatusCode} | Duration: {Duration}ms | Headers: {@Headers} | Body: {Body}",
                    requestId,
                    statusCode,
                    stopwatch.ElapsedMilliseconds,
                    responseHeaders,
                    TruncateBody(responseBodyText, 2000)
                );
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            Log.Error(
                ex,
                "🔴 Gateway Unhandled Exception | RequestId: {RequestId} | Path: {Path} | Duration: {Duration}ms",
                requestId,
                context.Request.Path,
                stopwatch.ElapsedMilliseconds
            );

            throw;
        }
        finally
        {
            context.Response.Body = originalResponseBody;
        }
    }

    private static async Task<string> ReadRequestBodyAsync(HttpRequest request)
    {
        request.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        request.Body.Seek(0, SeekOrigin.Begin);
        return TruncateBody(body, 2000);
    }

    private static string TruncateBody(string body, int maxLength)
    {
        if (string.IsNullOrEmpty(body))
            return string.Empty;

        return body.Length > maxLength
            ? body.Substring(0, maxLength) + "... (truncated)"
            : body;
    }
}
