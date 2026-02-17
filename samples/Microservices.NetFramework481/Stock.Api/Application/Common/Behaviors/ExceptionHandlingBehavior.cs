using MediatR;
using Newtonsoft.Json;
using Serilog;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sample.Product.NetFramework481.Application.Common.Behaviors;

/// <summary>
/// MediatR pipeline behavior for handling exceptions in Command/Query handlers
/// </summary>
public class ExceptionHandlingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private static readonly ILogger Logger = Log.ForContext<ExceptionHandlingBehavior<TRequest, TResponse>>();

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        var requestJson = SerializeToJson(request);

        try
        {
            Logger.Information("🔵 Executing {RequestName} | Request: {Request}", requestName, requestJson);
            var response = await next();
            var responseJson = SerializeToJson(response);
            Logger.Information("✅ Successfully executed {RequestName} | Response: {Response}", requestName, responseJson);
            return response;
        }
        catch (InvalidOperationException ex)
        {
            Logger.Error(ex, "🟡 Business Logic Exception in {RequestName} | Request: {Request} | Message: {Message}", requestName, requestJson, ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "🔴 Unhandled Exception in {RequestName} | Request: {Request} | ExceptionType: {ExceptionType} | Message: {Message}", requestName, requestJson, ex.GetType().Name, ex.Message);
            throw;
        }
    }

    private static string SerializeToJson(object obj)
    {
        try
        {
            if (obj == null) return "null";
            var json = JsonConvert.SerializeObject(obj, new JsonSerializerSettings { ReferenceLoopHandling = ReferenceLoopHandling.Ignore, MaxDepth = 5, NullValueHandling = NullValueHandling.Ignore });
            return json.Length > 2000 ? json.Substring(0, 2000) + "... (truncated)" : json;
        }
        catch (Exception ex)
        {
            return $"[Serialization Error: {ex.Message}]";
        }
    }
}
