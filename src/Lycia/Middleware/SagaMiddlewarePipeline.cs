using Lycia.Saga.Abstractions.Contexts;
using Lycia.Saga.Abstractions.Middlewares;
using Microsoft.Extensions.DependencyInjection;

namespace Lycia.Middleware;

/// <summary>Composes registered saga middleware into an ordered invocation delegate.</summary>
public sealed class SagaMiddlewarePipeline
{
    private readonly List<ISagaMiddleware> _middlewares;

    /// <summary>Creates a pipeline from resolved middleware and an optional explicit type order.</summary>
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

    /// <summary>Creates a pipeline by resolving middleware from the service provider in the supplied type order.</summary>
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

    /// <summary>Invokes each middleware in order and then executes the terminal handler delegate.</summary>
    public Task InvokeAsync(IInvocationContext context, Func<Task> terminal)
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
