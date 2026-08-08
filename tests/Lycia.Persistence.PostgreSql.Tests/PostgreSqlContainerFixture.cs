// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Testcontainers.PostgreSql;

namespace Lycia.Persistence.PostgreSql.Tests;

/// <summary>
/// Starts a single PostgreSQL container shared by every test in the conformance suite. Individual
/// tests stay isolated from each other because every conformance test operates on its own randomly
/// generated <c>sagaId</c>, so sharing the schema across tests within the container is safe.
/// </summary>
public class PostgreSqlContainerFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition("PostgreSqlContainer")]
public class PostgreSqlContainerCollection : ICollectionFixture<PostgreSqlContainerFixture>;
