// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Persistence.TestKit;
using Lycia.Saga.Abstractions.Outbox;

namespace Lycia.Persistence.InMemory.Tests;

public class InMemoryOutboxStoreConformanceTests : OutboxStoreConformanceTests
{
    protected override IOutboxStore CreateStore() => new InMemoryOutboxStore();
}
