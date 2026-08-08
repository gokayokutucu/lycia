// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Persistence.Relational.Internal.Migrations;
using Lycia.Persistence.TestKit;
using Lycia.Saga.Abstractions;

namespace Lycia.Persistence.SqlServer.Tests;

[Collection("SqlServerContainer")]
public class SqlServerSagaStoreConformanceTests(SqlServerContainerFixture fixture) : SagaStoreConformanceTests
{
    protected override ISagaStore CreateStore()
    {
        var options = new SqlServerSagaStoreOptions
        {
            ConnectionString = fixture.ConnectionString,
            SchemaManagement = SchemaManagementMode.ApplyMigrations
        };

        SqlServerSchemaMigrator.RunAsync(options).GetAwaiter().GetResult();

        return new SqlServerSagaStore(options, null!, null!, null!, null);
    }
}
