using System;
using Lycia.Saga.Abstractions; // To use the interface from Abstractions

namespace Lycia.Saga.Implementations.IdGeneration
{
    public class DefaultSagaIdGenerator : ISagaIdGenerator
    {
        public Guid GenerateNewSagaId() => Guid.NewGuid();
    }
}
