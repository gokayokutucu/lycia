namespace Lycia.Saga.Middleware;

public interface ISagaMiddleware
{
    Task InvokeAsync(SagaContextInvocationContext context, Func<Task> next);
}
