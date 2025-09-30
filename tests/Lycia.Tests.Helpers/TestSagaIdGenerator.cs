// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Saga.Utility;
using Lycia.Saga.Abstractions;

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