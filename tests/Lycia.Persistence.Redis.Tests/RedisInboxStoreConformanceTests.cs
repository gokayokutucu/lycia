// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Extensions.Configurations;
using Lycia.Persistence.TestKit;
using Lycia.Saga.Abstractions.Inbox;

namespace Lycia.Persistence.Redis.Tests;

[Collection(RedisSagaStoreCollection.Name)]
public class RedisInboxStoreConformanceTests(RedisSagaStoreFixture fixture) : InboxStoreConformanceTests
{
    protected override IInboxStore CreateStore()
    {
        var options = new InboxOptions
        {
            RetentionPeriod = TimeSpan.FromMinutes(5)
        };

        return new RedisInboxStore(fixture.Database, options);
    }
}
