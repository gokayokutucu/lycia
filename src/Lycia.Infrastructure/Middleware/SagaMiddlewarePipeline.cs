using Microsoft.Extensions.DependencyInjection;
using Lycia.Saga.Middleware;

namespace Lycia.Infrastructure.Middleware;

public sealed class SagaMiddlewarePipeline
{
    private readonly ISagaMiddleware[] _middlewares;

    public SagaMiddlewarePipeline(IEnumerable<ISagaMiddleware> middlewares, IServiceProvider sp, IReadOnlyList<Type>? orderedTypes = null)
    {
        if (orderedTypes == null || orderedTypes.Count == 0)
        {
            _middlewares = middlewares?.ToArray() ?? [];
        }
        else
        {
            // Resolve in specified order
            // Middlewares are registered as ISagaMiddleware, not as self; so resolve all and then order by type
            var list = new List<ISagaMiddleware>(orderedTypes.Count);
            var all = sp.GetServices<ISagaMiddleware>().ToList();

            list.AddRange(orderedTypes
                .Select(t => 
                    all.FirstOrDefault(m => m.GetType() == t) 
                    ?? all.FirstOrDefault(t.IsInstanceOfType))
                .OfType<ISagaMiddleware>());

            _middlewares = list.ToArray();
        }
    }

    public Task InvokeAsync(SagaContextInvocationContext context, Func<Task> terminal)
    {
        Func<Task> next = terminal;
        for (var i = _middlewares.Length - 1; i >= 0; i--)
        {
            var current = _middlewares[i];
            var innerNext = next;
            next = () => current.InvokeAsync(context, innerNext);
        }
        return next();
    }
}
