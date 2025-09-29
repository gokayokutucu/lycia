using Lycia.Abstractions;
using Lycia.Messaging;

namespace Lycia.Middleware;

/// <summary>
/// Context passed to ISagaMiddleware during saga handler invocation.
/// </summary>
public sealed class SagaContextInvocationContext
{
    public IMessage Message { get; set; } = null!;
    public ISagaContext? SagaContext { get; set; }
    public Type HandlerType { get; set; } = null!;
    public Guid? SagaId { get; set; }
    public CancellationToken CancellationToken { get; set; }
    public Exception? LastException { get; set; }
}
