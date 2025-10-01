namespace Lycia.Saga.Abstractions.Middlewares;

/// <summary>
/// Defines a middleware interface that aids in logging execution within a saga pipeline.
/// This middleware extends the capabilities of <c>ISagaMiddleware</c> and is used for logging purposes.
/// </summary>
public interface ILoggingSagaMiddleware : ISagaMiddleware;