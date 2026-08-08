// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Persistence.Relational.Internal.Migrations;
using Lycia.Persistence.TestKit;
using Lycia.Saga.Abstractions.Inbox;

namespace Lycia.Persistence.PostgreSql.Tests;

[Collection("PostgreSqlContainer")]
public class PostgreSqlInboxStoreConformanceTests(PostgreSqlContainerFixture fixture) : InboxStoreConformanceTests
{
    protected override IInboxStore CreateStore()
    {
        var options = new PostgreSqlInboxOptions
        {
            ConnectionString = fixture.ConnectionString,
            SchemaManagement = SchemaManagementMode.ApplyMigrations
        };

        PostgreSqlInboxOutboxSchemaMigrator.RunAsync(options.ConnectionString, options.SchemaName, options.SchemaManagement)
            .GetAwaiter().GetResult();

        return new PostgreSqlInboxStore(options);
    }
}
