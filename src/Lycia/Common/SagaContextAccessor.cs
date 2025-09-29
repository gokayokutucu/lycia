using Lycia.Abstractions;

namespace Lycia.Common;

public sealed class SagaContextAccessor : ISagaContextAccessor
{
    public ISagaContext? Current { get; set; }
}
