// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using System.Reflection;
using Lycia.Persistence.Relational.Internal.Migrations;
using Npgsql;

namespace Lycia.Persistence.PostgreSql;

/// <summary>
/// Applies the embedded PostgreSQL Inbox/Outbox schema via the shared migration runner. Kept separate
/// from <see cref="PostgreSqlSchemaMigrator"/> (SagaStore) so SagaStore-only users never provision
/// Inbox/Outbox tables; only invoked from the Inbox/Outbox DSL extension methods.
/// </summary>
public static class PostgreSqlInboxOutboxSchemaMigrator
{
    /// <summary>Applies pending Inbox/Outbox migrations without recreating existing tables.</summary>
    public static Task RunAsync(string connectionString, string schemaName, SchemaManagementMode schemaManagement,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string must not be empty.", nameof(connectionString));

        var dialect = new RelationalMigrationDialect(
            ensureTrackingTableSql:
            $"""
             CREATE SCHEMA IF NOT EXISTS {schemaName};
             CREATE TABLE IF NOT EXISTS __lycia_schema_migrations (
                 name varchar(400) NOT NULL PRIMARY KEY,
                 applied_at_utc timestamptz NOT NULL DEFAULT now()
             );
             """,
            selectAppliedNamesSql: "SELECT name FROM __lycia_schema_migrations;",
            insertAppliedNameSql: "INSERT INTO __lycia_schema_migrations (name) VALUES (@name);");

        var scripts = new List<RelationalMigrationScript>
        {
            new("002_InboxOutboxSchema", ReadEmbeddedScript("002_InboxOutboxSchema.sql"))
        };

        var effectiveConnectionString = schemaName == "public"
            ? connectionString
            : new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = schemaName }.ConnectionString;

        return RelationalMigrationRunner.RunAsync(
            () => new NpgsqlConnection(effectiveConnectionString),
            scripts,
            dialect,
            schemaManagement,
            cancellationToken);
    }

    private static string ReadEmbeddedScript(string fileName)
    {
        var assembly = typeof(PostgreSqlInboxOutboxSchemaMigrator).GetTypeInfo().Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Embedded migration script '{fileName}' was not found.");

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded migration script '{fileName}' could not be opened.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
