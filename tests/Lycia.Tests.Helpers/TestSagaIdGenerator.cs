using Lycia.Messaging.Utility;
using Lycia.Saga.Extensions;

namespace Lycia.Tests.Helpers;

/// <summary>
/// Provides a consistent SagaId for testing purposes.
/// </summary>
public class TestSagaIdGenerator(Guid fixedId) : ISagaIdGenerator
{
    public Guid FixedId { get; } = fixedId;

    /// <summary>
    /// Creates a new generator with a random version 7 Guid.
    /// </summary>
    public TestSagaIdGenerator() : this(GuidV7.NewGuidV7()) { }

    public Guid Generate() => FixedId;

    /// <summary>
    /// Factory method to create a generator with a known Guid.
    /// </summary>
    public static TestSagaIdGenerator From(Guid fixedGuid) => new(fixedGuid);
}