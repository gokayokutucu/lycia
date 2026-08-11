// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using System.Reflection;
using Lycia.Persistence.Relational.Internal.Migrations;
using Npgsql;

namespace Lycia.Persistence.PostgreSql;

/// <summary>Applies the embedded PostgreSQL schema for <see cref="PostgreSqlSagaStore"/> via the shared migration runner.</summary>
public static class PostgreSqlSchemaMigrator
{
    /// <summary>Applies pending SagaStore migrations according to the configured schema-management mode.</summary>
    public static Task RunAsync(PostgreSqlSagaStoreOptions options, CancellationToken cancellationToken = default)
    {
        if (options == null) throw new ArgumentNullException(nameof(options));

        var schema = options.SchemaName;

        // Table/tracking references are left unqualified in the SQL text; SchemaName is instead injected
        // as the connection's SearchPath (see BuildEffectiveConnectionString), so non-default schemas never
        // require rewriting identifiers embedded inside other identifiers (e.g. index names).
        var dialect = new RelationalMigrationDialect(
            ensureTrackingTableSql:
            $"""
             CREATE SCHEMA IF NOT EXISTS {schema};
             CREATE TABLE IF NOT EXISTS __lycia_schema_migrations (
                 name varchar(400) NOT NULL PRIMARY KEY,
                 applied_at_utc timestamptz NOT NULL DEFAULT now()
             );
             """,
            selectAppliedNamesSql: "SELECT name FROM __lycia_schema_migrations;",
            insertAppliedNameSql: "INSERT INTO __lycia_schema_migrations (name) VALUES (@name);");

        var scripts = new List<RelationalMigrationScript>
        {
            new("001_InitialSchema", ReadEmbeddedScript("001_InitialSchema.sql"))
        };

        return RelationalMigrationRunner.RunAsync(
            () => new NpgsqlConnection(options.BuildEffectiveConnectionString()),
            scripts,
            dialect,
            options.SchemaManagement,
            cancellationToken);
    }

    private static string ReadEmbeddedScript(string fileName)
    {
        var assembly = typeof(PostgreSqlSchemaMigrator).GetTypeInfo().Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Embedded migration script '{fileName}' was not found.");

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded migration script '{fileName}' could not be opened.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
