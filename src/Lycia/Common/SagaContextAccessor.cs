using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Contexts;

namespace Lycia.Common;

/// <summary>Exposes the saga context associated with the current asynchronous execution flow.</summary>
public sealed class SagaContextAccessor : ISagaContextAccessor
{
    /// <inheritdoc />
    public ISagaContext? Current { get; set; }
}
