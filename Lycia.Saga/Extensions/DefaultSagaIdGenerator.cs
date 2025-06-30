using   Lycia.Messaging.Extensions;

namespace Lycia.Saga.Extensions;

public interface ISagaIdGenerator
{
    Guid Generate();
}

public class DefaultSagaIdGenerator : ISagaIdGenerator
{
    public Guid Generate() =>
#if NET9_0_OR_GREATER
        Guid.CreateVersion7();
#else
        GuidExtensions.CreateVersion7(); 
#endif
}