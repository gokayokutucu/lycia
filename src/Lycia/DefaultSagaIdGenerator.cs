// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using Lycia.Saga.Abstractions;
using Lycia.Saga.Utility;

namespace Lycia;

/// <summary>
/// Represents the default implementation of the <see cref="ISagaIdGenerator"/> interface,
/// responsible for generating unique identifiers for sagas using a version 7 GUID.
/// </summary>
public class DefaultSagaIdGenerator : ISagaIdGenerator
{
    public Guid Generate() =>
#if NET9__OR_GREATER
        Guid.CreateVersion7(); 
#else
        GuidV7.NewGuidV7();
#endif
}