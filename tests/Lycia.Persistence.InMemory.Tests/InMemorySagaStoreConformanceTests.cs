// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Persistence.TestKit;
using Lycia.Saga.Abstractions;
using Lycia.Stores;

namespace Lycia.Persistence.InMemory.Tests;

public class InMemorySagaStoreConformanceTests : SagaStoreConformanceTests
{
    protected override ISagaStore CreateStore() => new InMemorySagaStore(null!, null!, null!);
}
