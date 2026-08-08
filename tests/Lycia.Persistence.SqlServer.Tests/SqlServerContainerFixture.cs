// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Testcontainers.MsSql;

namespace Lycia.Persistence.SqlServer.Tests;

/// <summary>
/// Starts a single SQL Server container shared by every test in the conformance suite. Individual
/// tests stay isolated from each other because every conformance test operates on its own randomly
/// generated <c>sagaId</c>, so sharing the schema across tests within the container is safe.
/// </summary>
public class SqlServerContainerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition("SqlServerContainer")]
public class SqlServerContainerCollection : ICollectionFixture<SqlServerContainerFixture>;
