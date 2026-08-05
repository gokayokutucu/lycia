using Lycia.Saga.Abstractions.Contexts;
using Lycia.Saga.Abstractions.Messaging;

namespace Lycia.Contexts;

/// <summary>
/// Context passed to ISagaMiddleware during saga handler invocation.
/// </summary>
public sealed class SagaContextInvocationContext : IInvocationContext
{
    /// <inheritdoc />
    public IMessage Message { get; set; } = null!;
    /// <inheritdoc />
    public ISagaContext? SagaContext { get; set; }
    /// <inheritdoc />
    public Type HandlerType { get; set; } = null!;
    /// <inheritdoc />
    public Guid? SagaId { get; set; }
    /// <inheritdoc />
    public string ApplicationId { get; set; } = string.Empty;
    /// <inheritdoc />
    public CancellationToken CancellationToken { get; set; }
    /// <inheritdoc />
    public Exception? LastException { get; set; }
}
