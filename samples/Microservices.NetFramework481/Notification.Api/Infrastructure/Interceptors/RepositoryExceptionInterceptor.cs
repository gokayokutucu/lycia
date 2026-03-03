using Castle.DynamicProxy;
using Newtonsoft.Json;
using Serilog;
using System;
using System.Linq;

namespace Sample.Notification.NetFramework481.Infrastructure.Interceptors;

public class RepositoryExceptionInterceptor : IInterceptor
{
    private static readonly ILogger Logger = Log.ForContext<RepositoryExceptionInterceptor>();

    public void Intercept(IInvocation invocation)
    {
        var repositoryName = invocation.TargetType.Name;
        var methodName = invocation.Method.Name;
        var arguments = SerializeArguments(invocation.Arguments);

        try
        {
            Logger.Information(
                "🔵 Repository Call: {Repository}.{Method} | Arguments: {Arguments}",
                repositoryName,
                methodName,
                arguments
            );

            invocation.Proceed();

            var returnValue = SerializeToJson(invocation.ReturnValue);

            Logger.Information(
                "✅ Repository Call Success: {Repository}.{Method} | ReturnValue: {ReturnValue}",
                repositoryName,
                methodName,
                returnValue
            );
        }
        catch (Exception ex)
        {
            Logger.Error(
                ex,
                "🔴 Repository Exception: {Repository}.{Method} | Arguments: {Arguments} | ExceptionType: {ExceptionType} | Message: {Message}",
                repositoryName,
                methodName,
                arguments,
                ex.GetType().Name,
                ex.Message
            );
            throw;
        }
    }

    private static string SerializeArguments(object[] args)
    {
        try
        {
            if (args == null || args.Length == 0)
                return "[]";

            var argList = args.Select((arg, index) => new
            {
                Index = index,
                Type = arg?.GetType().Name ?? "null",
                Value = SerializeToJson(arg)
            }).ToArray();

            return JsonConvert.SerializeObject(argList, new JsonSerializerSettings
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                MaxDepth = 3,
                NullValueHandling = NullValueHandling.Include
            });
        }
        catch (Exception ex)
        {
            return $"[Serialization Error: {ex.Message}]";
        }
    }

    private static string SerializeToJson(object obj)
    {
        try
        {
            if (obj == null) return "null";

            // Handle Task results
            var type = obj.GetType();
            if (type.IsGenericType && type.GetGenericTypeDefinition().Name.Contains("Task"))
            {
                return "[Task - not awaited yet]";
            }

            var json = JsonConvert.SerializeObject(obj, new JsonSerializerSettings
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                MaxDepth = 3,
                NullValueHandling = NullValueHandling.Ignore
            });

            return json.Length > 1000
                ? json.Substring(0, 1000) + "... (truncated)"
                : json;
        }
        catch (Exception ex)
        {
            return $"[Serialization Error: {ex.Message}]";
        }
    }
}
