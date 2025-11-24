namespace Lycia.Saga.Abstractions;

public interface ISagaIdGenerator
{
    Guid Generate();
}