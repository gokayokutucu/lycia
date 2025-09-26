namespace Lycia.Saga.Middleware;

public sealed class SagaMiddlewareOptions
{
    private readonly List<Type?> _middlewares = new();
    public void AddMiddleware<T>() where T : ISagaMiddleware => _middlewares.Add(typeof(T));
    public IReadOnlyList<Type?> Middlewares => _middlewares;
}
