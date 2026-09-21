// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
using System.Reflection;
using Lycia.Persistence.Relational.Internal.Migrations;
using Npgsql;

namespace Lycia.Persistence.PostgreSql;

internal static class PostgreSqlReconciliationSchemaMigrator
{
    public static Task RunAsync(PostgreSqlSagaStoreOptions options, CancellationToken cancellationToken = default)
    {
        var dialect = new RelationalMigrationDialect(
            $"CREATE SCHEMA IF NOT EXISTS {options.SchemaName}; CREATE TABLE IF NOT EXISTS __lycia_schema_migrations (name varchar(400) NOT NULL PRIMARY KEY, applied_at_utc timestamptz NOT NULL DEFAULT now());",
            "SELECT name FROM __lycia_schema_migrations;",
            "INSERT INTO __lycia_schema_migrations (name) VALUES (@name);");
        return RelationalMigrationRunner.RunAsync(() => new NpgsqlConnection(options.BuildEffectiveConnectionString()),
            [new RelationalMigrationScript("003_SplitStoreReconciliation", ReadScript())], dialect,
            options.SchemaManagement, cancellationToken);
    }

    private static string ReadScript()
    {
        var assembly = typeof(PostgreSqlReconciliationSchemaMigrator).GetTypeInfo().Assembly;
        var name = assembly.GetManifestResourceNames().First(x =>
            x.EndsWith("003_SplitStoreReconciliation.sql", StringComparison.OrdinalIgnoreCase));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
