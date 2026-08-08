// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Persistence.Relational.Internal.Migrations;
using Lycia.Persistence.TestKit;
using Lycia.Saga.Abstractions;

namespace Lycia.Persistence.PostgreSql.Tests;

[Collection("PostgreSqlContainer")]
public class PostgreSqlSagaStoreConformanceTests(PostgreSqlContainerFixture fixture) : SagaStoreConformanceTests
{
    protected override ISagaStore CreateStore()
    {
        var options = new PostgreSqlSagaStoreOptions
        {
            ConnectionString = fixture.ConnectionString,
            SchemaManagement = SchemaManagementMode.ApplyMigrations
        };

        PostgreSqlSchemaMigrator.RunAsync(options).GetAwaiter().GetResult();

        return new PostgreSqlSagaStore(options, null!, null!, null!, null);
    }
}
