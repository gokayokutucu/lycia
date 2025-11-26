using Lycia.Saga.Abstractions.Contexts;
using Lycia.Saga.Abstractions.Messaging;

namespace Lycia.Contexts;

/// <summary>
/// Context passed to ISagaMiddleware during saga handler invocation.
/// </summary>
public sealed class SagaContextInvocationContext : IInvocationContext
{
    public IMessage Message { get; set; } = null!;
    public ISagaContext? SagaContext { get; set; }
    public Type HandlerType { get; set; } = null!;
    public Guid? SagaId { get; set; }
    public string ApplicationId { get; set; }
    public CancellationToken CancellationToken { get; set; }
    public Exception? LastException { get; set; }
}