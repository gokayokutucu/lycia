// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
using Lycia.Persistence.TestKit;
using Lycia.Saga.Abstractions.Persistence.Journal;

namespace Lycia.Persistence.InMemory.Tests;

public class InMemorySagaJournalStoreConformanceTests : SagaJournalStoreConformanceTests
{
    protected override ISagaJournalStore CreateStore() => new InMemorySagaJournalStore();
}
