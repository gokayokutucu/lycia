// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using System.Linq;
using System.Reflection;
using Lycia.Persistence.Relational.Internal.Migrations;
using Microsoft.Data.SqlClient;

namespace Lycia.Persistence.SqlServer;

/// <summary>Applies the embedded SQL Server schema for <see cref="SqlServerSagaStore"/> via the shared migration runner.</summary>
public static class SqlServerSchemaMigrator
{
    public static Task RunAsync(SqlServerSagaStoreOptions options, CancellationToken cancellationToken = default)
    {
        if (options == null) throw new ArgumentNullException(nameof(options));

        var schema = options.SchemaName;
        var trackingTable = $"{schema}.__LyciaSchemaMigrations";

        var dialect = new RelationalMigrationDialect(
            ensureTrackingTableSql:
            $"""
             IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = '{schema}')
                 EXEC('CREATE SCHEMA {schema}');
             IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id
                             WHERE s.name = '{schema}' AND t.name = '__LyciaSchemaMigrations')
                 CREATE TABLE {trackingTable} (
                     Name NVARCHAR(400) NOT NULL PRIMARY KEY,
                     AppliedAtUtc DATETIME2(3) NOT NULL CONSTRAINT DF___LyciaSchemaMigrations_AppliedAtUtc DEFAULT (SYSUTCDATETIME())
                 );
             """,
            selectAppliedNamesSql: $"SELECT Name FROM {trackingTable};",
            insertAppliedNameSql: $"INSERT INTO {trackingTable} (Name) VALUES (@name);");

        var scripts = new List<RelationalMigrationScript>
        {
            new("001_InitialSchema", ReadEmbeddedScript("001_InitialSchema.sql", schema))
        };

        return RelationalMigrationRunner.RunAsync(
            () => new SqlConnection(options.ConnectionString),
            scripts,
            dialect,
            options.SchemaManagement,
            cancellationToken);
    }

    private static string ReadEmbeddedScript(string fileName, string schemaName)
    {
        var assembly = typeof(SqlServerSchemaMigrator).GetTypeInfo().Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Embedded migration script '{fileName}' was not found.");

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded migration script '{fileName}' could not be opened.");
        using var reader = new StreamReader(stream);
        var sql = reader.ReadToEnd();

        return schemaName == "dbo" ? sql : sql.Replace("dbo.", $"{schemaName}.");
    }
}
