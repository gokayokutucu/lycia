using Lycia.Saga.Abstractions;

namespace Lycia.Saga.Common;

public sealed class SagaContextAccessor : ISagaContextAccessor
{
    public ISagaContext? Current { get; set; }
}
