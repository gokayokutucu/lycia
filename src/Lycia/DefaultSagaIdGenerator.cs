// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using Lycia.Saga.Abstractions;
using Lycia.Saga.Utility;

namespace Lycia;

public class DefaultSagaIdGenerator : ISagaIdGenerator
{
    public Guid Generate() => GuidV7.NewGuidV7();
}