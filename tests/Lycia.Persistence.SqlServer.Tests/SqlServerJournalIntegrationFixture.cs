// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Testcontainers.MsSql;
using Testcontainers.Redis;

namespace Lycia.Persistence.SqlServer.Tests;

/// <summary>
/// Starts one SQL Server container and one Redis container shared by the Split Store + journal
/// integration tests, which need a real canonical relational store and a real rebuildable
/// operational projection store at the same time.
/// </summary>
public sealed class SqlServerJournalIntegrationFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _sql = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .WithCleanUp(true)
        .Build();

    public string SqlConnectionString => _sql.GetConnectionString();
    public string RedisConnectionString => _redis.GetConnectionString();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_sql.StartAsync(), _redis.StartAsync());
    }

    public async Task DisposeAsync()
    {
        await Task.WhenAll(_sql.DisposeAsync().AsTask(), _redis.DisposeAsync().AsTask());
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SqlServerJournalIntegrationCollection : ICollectionFixture<SqlServerJournalIntegrationFixture>
{
    public const string Name = "SqlServer+Redis journal integration";
}
