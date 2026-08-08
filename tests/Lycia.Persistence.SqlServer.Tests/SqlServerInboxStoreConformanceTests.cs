// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Persistence.Relational.Internal.Migrations;
using Lycia.Persistence.TestKit;
using Lycia.Saga.Abstractions.Inbox;

namespace Lycia.Persistence.SqlServer.Tests;

[Collection("SqlServerContainer")]
public class SqlServerInboxStoreConformanceTests(SqlServerContainerFixture fixture) : InboxStoreConformanceTests
{
    protected override IInboxStore CreateStore()
    {
        var connectionString = fixture.ConnectionString;
        SqlServerInboxOutboxSchemaMigrator.RunAsync(connectionString, "dbo", SchemaManagementMode.ApplyMigrations)
            .GetAwaiter().GetResult();

        return new SqlServerInboxStore(new SqlServerInboxOptions { ConnectionString = connectionString });
    }
}
