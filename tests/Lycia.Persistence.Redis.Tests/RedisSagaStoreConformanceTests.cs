// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Extensions.Configurations;
using Lycia.Persistence.TestKit;
using Lycia.Saga.Abstractions;

namespace Lycia.Persistence.Redis.Tests;

[Collection(RedisSagaStoreCollection.Name)]
public class RedisSagaStoreConformanceTests(RedisSagaStoreFixture fixture) : SagaStoreConformanceTests
{
    protected override ISagaStore CreateStore()
    {
        var options = new SagaStoreOptions
        {
            ApplicationId = "RedisConformanceTests",
            StepLogTtl = TimeSpan.FromMinutes(5)
        };

        return new RedisSagaStore(fixture.Database, null!, null!, null!, options);
    }
}
