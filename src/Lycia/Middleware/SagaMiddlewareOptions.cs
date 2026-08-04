using Lycia.Saga.Abstractions.Middlewares;

namespace Lycia.Middleware;

/// <summary>Collects the ordered saga middleware types used when building an invocation pipeline.</summary>
public sealed class SagaMiddlewareOptions
{
    private readonly List<Type?> _middlewares = new();
    /// <summary>Adds a middleware type to the end of the configured execution order.</summary>
    public void AddMiddleware<T>() where T : ISagaMiddleware => _middlewares.Add(typeof(T));
    /// <summary>Gets the middleware types in configured execution order.</summary>
    public IReadOnlyList<Type?> Middlewares => _middlewares;
}
