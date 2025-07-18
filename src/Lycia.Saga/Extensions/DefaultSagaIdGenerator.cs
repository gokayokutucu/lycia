using Lycia.Messaging.Extensions;
using Lycia.Messaging.Utility;

namespace Lycia.Saga.Extensions;

public interface ISagaIdGenerator
{
    Guid Generate();
}

public class DefaultSagaIdGenerator : ISagaIdGenerator
{
    public Guid Generate() =>
#if NET9__OR_GREATER
        Guid.CreateVersion7(); 
#else
        GuidV7.NewGuidV7();
#endif
}