// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using StackExchange.Redis;
using Testcontainers.Redis;

namespace Lycia.Persistence.Redis.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RedisSagaStoreCollection : ICollectionFixture<RedisSagaStoreFixture>
{
    public const string Name = "Redis saga store";
}

public sealed class RedisSagaStoreFixture : IAsyncLifetime
{
    private readonly RedisContainer _container = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .WithCleanUp(true)
        .Build();

    private ConnectionMultiplexer? _connection;
    public IConnectionMultiplexer Connection => _connection
        ?? throw new InvalidOperationException("Redis fixture has not been initialized.");
    public IDatabase Database { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _connection = await ConnectionMultiplexer.ConnectAsync(_container.GetConnectionString());
        Database = _connection.GetDatabase();
    }

    public async Task DisposeAsync()
    {
        if (_connection is not null)
            await _connection.DisposeAsync();
        await _container.DisposeAsync();
    }
}
