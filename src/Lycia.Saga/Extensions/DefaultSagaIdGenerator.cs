using Lycia.Messaging.Utility;

namespace Lycia.Saga.Extensions;

public interface ISagaIdGenerator
{
    Guid Generate();
}

public class DefaultSagaIdGenerator : ISagaIdGenerator
{
    public Guid Generate() => GuidV7.NewGuidV7();
}