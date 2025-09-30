using Lycia.Saga.Abstractions.Contexts;

namespace Lycia.Saga.Abstractions.Middlewares;

public interface ISagaMiddleware
{
    Task InvokeAsync(IInvocationContext context, Func<Task> next);
}
