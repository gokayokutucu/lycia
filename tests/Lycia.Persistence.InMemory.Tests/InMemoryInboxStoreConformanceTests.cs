// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Persistence.TestKit;
using Lycia.Saga.Abstractions.Inbox;

namespace Lycia.Persistence.InMemory.Tests;

public class InMemoryInboxStoreConformanceTests : InboxStoreConformanceTests
{
    protected override IInboxStore CreateStore() => new InMemoryInboxStore();
}
