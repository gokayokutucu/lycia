// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
using StackExchange.Redis;
using Testcontainers.Redis;

namespace Lycia.Persistence.PostgreSql.Tests;

/// <summary>
/// Starts a Redis container for the Split Store + journal integration tests in this project, which
/// need both a real PostgreSQL canonical store (via <see cref="PostgreSqlContainerFixture"/>) and a
/// real Redis operational projection.
/// </summary>
public sealed class RedisContainerFixture : IAsyncLifetime
{
    private readonly RedisContainer _container = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .WithCleanUp(true)
        .Build();

    private ConnectionMultiplexer? _connection;
    public string ConnectionString => _container.GetConnectionString();
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
