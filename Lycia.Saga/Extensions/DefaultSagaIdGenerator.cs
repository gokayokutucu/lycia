namespace Lycia.Saga.Extensions;

public interface ISagaIdGenerator
{
    Guid Generate();
}

public class DefaultSagaIdGenerator : ISagaIdGenerator
{
    public Guid Generate() => Guid.CreateVersion7(); // Changed to NewGuid()
}