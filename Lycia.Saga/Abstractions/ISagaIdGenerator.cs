using System;

namespace Lycia.Saga.Abstractions
{
    /// <summary>
    /// Defines a contract for generating unique saga identifiers.
    /// </summary>
    public interface ISagaIdGenerator
    {
        /// <summary>
        /// Generates a new unique identifier for a saga instance.
        /// </summary>
        /// <returns>A new <see cref="Guid"/> to be used as a saga ID.</returns>
        Guid GenerateNewSagaId(); // Changed method name
    }
}
