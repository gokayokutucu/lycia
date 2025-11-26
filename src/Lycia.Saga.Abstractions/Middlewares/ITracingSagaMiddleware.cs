namespace Lycia.Saga.Abstractions.Middlewares;

/// <summary>
/// Marker interface for saga middlewares that are responsible for tracing / Activity spans.
/// Used by the middleware pipeline to place tracing in a dedicated slot.
/// </summary>
public interface ITracingSagaMiddleware : ISagaMiddleware;