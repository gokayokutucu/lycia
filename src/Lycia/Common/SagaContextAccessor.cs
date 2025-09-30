using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Contexts;

namespace Lycia.Common;

public sealed class SagaContextAccessor : ISagaContextAccessor
{
    public ISagaContext? Current { get; set; }
}
