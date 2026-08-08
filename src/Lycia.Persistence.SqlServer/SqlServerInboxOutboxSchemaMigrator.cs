// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Persistence.Relational.Internal.Migrations;
using Microsoft.Data.SqlClient;

namespace Lycia.Persistence.SqlServer;

/// <summary>
/// Applies the embedded Inbox/Outbox schema via the shared migration runner. Run independently from
/// <see cref="SqlServerSchemaMigrator"/> so a caller that only enables the SagaStore never pays for
/// Inbox/Outbox tables it does not use.
/// </summary>
public static class SqlServerInboxOutboxSchemaMigrator
{
    /// <summary>Applies the Inbox/Outbox schema (tables <c>LyciaInbox</c>/<c>LyciaOutbox</c>) for <paramref name="schemaName"/>.</summary>
    public static Task RunAsync(string connectionString, string schemaName, SchemaManagementMode schemaManagement,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string must not be empty.", nameof(connectionString));
        if (string.IsNullOrWhiteSpace(schemaName))
            throw new ArgumentException("Schema name must not be empty.", nameof(schemaName));

        var scripts = new List<RelationalMigrationScript>
        {
            new("002_InboxOutboxSchema",
                SqlServerSchemaMigrator.ReadEmbeddedScript("002_InboxOutboxSchema.sql", schemaName))
        };

        return RelationalMigrationRunner.RunAsync(
            () => new SqlConnection(connectionString),
            scripts,
            SqlServerSchemaMigrator.CreateDialect(schemaName),
            schemaManagement,
            cancellationToken);
    }
}
