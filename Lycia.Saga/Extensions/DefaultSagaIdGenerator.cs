using   Lycia.Messaging.Extensions;

namespace Lycia.Saga.Extensions;

public interface ISagaIdGenerator
{
    Guid Generate();
}

public class DefaultSagaIdGenerator : ISagaIdGenerator
{
    public Guid Generate() => GuidExtensions.CreateVersion7();
}