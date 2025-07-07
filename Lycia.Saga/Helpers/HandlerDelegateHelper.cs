using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace Lycia.Saga.Helpers;

public static class HandlerDelegateHelper
{
    private static readonly ConcurrentDictionary<(Type HandlerType, string MethodName), Func<object, object, Task>> DelegateCache = new();

    // Handles both real handlers and proxy/mock types (e.g., Moq) by searching for the method in both the class and its interfaces.
    // This is needed because proxies may implement the method only via interface, not directly on the proxy class.
    public static Func<object, object, Task> GetHandlerDelegate(Type handlerType, string methodName, Type messageType)
    {
        var key = (handlerType, methodName);
        if (DelegateCache.TryGetValue(key, out var dlg))
            return dlg;

        // Try to get the method directly from the type (including non-public for concrete handler types)
        var method = handlerType.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
            [messageType], null);

        // If not found, look in all implemented interfaces (useful for Moq/proxy types)
        if (method == null)
        {
            foreach (var iface in handlerType.GetInterfaces())
            {
                method = iface.GetMethod(methodName, [messageType]);
                if (method != null)
                    break;
            }
        }

        if (method == null)
            throw new InvalidOperationException($"Method '{methodName}' not found on {handlerType.FullName}");

        var handlerParam = Expression.Parameter(typeof(object), "handler");
        var messageParam = Expression.Parameter(typeof(object), "message");

        // Add type checks to ensure the parameters are of the correct types
        var body = Expression.Block(
            Expression.IfThen(
                Expression.Not(Expression.TypeIs(messageParam, messageType)),
                Expression.Throw(Expression.New(typeof(InvalidCastException)))
            ),
            Expression.Call(
                Expression.Convert(handlerParam, method.DeclaringType!),
                method,
                Expression.Convert(messageParam, messageType)
            )
        );

        var lambda = Expression.Lambda<Func<object, object, Task>>(body, handlerParam, messageParam);
        var compiled = lambda.Compile();
        DelegateCache[key] = compiled;
        return compiled;
    }
}