using Microsoft.Extensions.DependencyInjection;
using Lycia.Saga.Middleware;

namespace Lycia.Infrastructure.Middleware;

public sealed class SagaMiddlewarePipeline
{
    private readonly List<ISagaMiddleware> _middlewares;

    // Constructor used by dispatcher: pass resolved middlewares and optional ordered types
    public SagaMiddlewarePipeline(
        IEnumerable<ISagaMiddleware> middlewares,
        IServiceProvider serviceProvider,
        IReadOnlyList<Type>? orderedTypes = null)
    {
        if (orderedTypes == null || orderedTypes.Count == 0)
        {
            _middlewares = middlewares.ToList();
            return;
        }

        var all = serviceProvider.GetServices<ISagaMiddleware>().ToList();
        _middlewares = OrderByTypes(all, orderedTypes);
    }

    // Back-compat constructor used by some tests: pass only ordered types and service provider
    public SagaMiddlewarePipeline(
        IEnumerable<Type> orderedTypes,
        IServiceProvider serviceProvider)
    {
        var types = orderedTypes.ToArray();
        var all = serviceProvider.GetServices<ISagaMiddleware>().ToList();
        _middlewares = OrderByTypes(all, types);
    }

    private static List<ISagaMiddleware> OrderByTypes(List<ISagaMiddleware> all, IReadOnlyList<Type> orderedTypes)
    {
        var list = new List<ISagaMiddleware>(orderedTypes.Count);

        foreach (var t in orderedTypes)
        {
            var match = all.FirstOrDefault(m => m.GetType() == t)
                        ?? all.FirstOrDefault(t.IsInstanceOfType);
            if (match != null && !list.Contains(match))
                list.Add(match);
        }

        return list;
    }

    public Task InvokeAsync(SagaContextInvocationContext context, Func<Task> terminal)
    {
        var next = terminal;
        for (var i = _middlewares.Count - 1; i >= 0; i--)
        {
            var current = _middlewares[i];
            var innerNext = next;
            next = () => current.InvokeAsync(context, innerNext);
        }
        return next();
    }
}
