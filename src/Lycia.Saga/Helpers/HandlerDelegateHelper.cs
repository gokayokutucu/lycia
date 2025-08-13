using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace Lycia.Saga.Helpers;

public static class HandlerDelegateHelper
{
    private static readonly ConcurrentDictionary<(Type HandlerType, string MethodName, Type MessageType), Func<object, object, CancellationToken, Task>> DelegateCache = new();

    public static Func<object, object, CancellationToken, Task> GetHandlerDelegate(Type handlerType, string methodName, Type messageType)
    {
        var key = (handlerType, methodName, messageType);
        if (DelegateCache.TryGetValue(key, out var dlg))
            return dlg;

        // Try to find the method directly on the concrete type (public or non-public)
        var method = FindMethod(handlerType, methodName, messageType, withCancellationToken: true)
                     ?? FindMethod(handlerType, methodName, messageType, withCancellationToken: false)
                     ?? FindOnInterfaces(handlerType, methodName, messageType, withCancellationToken: true)
                     ?? FindOnInterfaces(handlerType, methodName, messageType, withCancellationToken: false);

        if (method == null)
            throw new InvalidOperationException($"Method '{methodName}({messageType.FullName})' not found on {handlerType.FullName}.");

        var handlerParam = Expression.Parameter(typeof(object), "handler");
        var messageParam = Expression.Parameter(typeof(object), "message");
        var ctParam = Expression.Parameter(typeof(CancellationToken), "cancellationToken");

        // Build a defensive type check with a helpful error message
        var expectedTypeConst = Expression.Constant(messageType, typeof(Type));
        var actualTypeExpr = Expression.Call(messageParam, typeof(object).GetMethod(nameof(object.GetType))!);
        var invalidCastCtor = typeof(InvalidCastException).GetConstructor(new[] { typeof(string) })!;
        var errorMsg = Expression.Call(
            typeof(string).GetMethod(nameof(string.Format), [typeof(string), typeof(object), typeof(object)])!,
            Expression.Constant("HandlerDelegateHelper: cannot cast message. Expected={0}, Actual={1}"),
            expectedTypeConst,
            Expression.Convert(actualTypeExpr, typeof(object))
        );

        var callArgs = method.GetParameters().Length == 2
            ? new Expression[] { Expression.Convert(messageParam, messageType), ctParam }
            : new Expression[] { Expression.Convert(messageParam, messageType) };

        var body = Expression.Block(
            Expression.IfThen(
                Expression.Not(Expression.TypeIs(messageParam, messageType)),
                Expression.Throw(Expression.New(invalidCastCtor, errorMsg))
            ),
            Expression.Call(
                Expression.Convert(handlerParam, method.DeclaringType!),
                method,
                callArgs
            )
        );

        var lambda = Expression.Lambda<Func<object, object, CancellationToken, Task>>(body, handlerParam, messageParam, ctParam);
        var compiled = lambda.Compile();
        DelegateCache[key] = compiled;
        return compiled;
    }

    private static MethodInfo? FindMethod(Type handlerType, string methodName, Type messageType, bool withCancellationToken)
    {
        var paramTypes = withCancellationToken
            ? new[] { messageType, typeof(CancellationToken) }
            : new[] { messageType };

        return handlerType.GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: paramTypes,
            modifiers: null);
    }

    private static MethodInfo? FindOnInterfaces(Type handlerType, string methodName, Type messageType, bool withCancellationToken)
    {
        foreach (var iface in handlerType.GetInterfaces())
        {
            var paramTypes = withCancellationToken
                ? new[] { messageType, typeof(CancellationToken) }
                : new[] { messageType };
            var m = iface.GetMethod(methodName, paramTypes);
            if (m != null)
                return m;
        }
        return null;
    }
}