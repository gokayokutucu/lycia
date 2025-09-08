// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
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