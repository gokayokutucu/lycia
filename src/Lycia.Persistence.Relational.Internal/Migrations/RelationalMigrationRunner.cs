// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using System.Data;
using System.Data.Common;

namespace Lycia.Persistence.Relational.Internal.Migrations;

/// <summary>
/// Applies an ordered list of named SQL migration scripts against any ADO.NET provider, tracking which
/// scripts have already run in a small provider-owned tracking table. Driver-agnostic: it only talks to
/// <see cref="DbConnection"/>/<see cref="DbCommand"/>, so it works unchanged for Microsoft.Data.SqlClient
/// and Npgsql alike. Each provider package supplies its own <see cref="RelationalMigrationDialect"/> and
/// migration scripts; this runner owns only the apply/validate/skip orchestration.
/// </summary>
/// <remarks>
/// Transaction begin/commit use the synchronous <see cref="DbConnection.BeginTransaction()"/> API rather
/// than the async overload, because this project also targets netstandard2.0, whose <see cref="DbConnection"/>
/// contract predates <c>BeginTransactionAsync</c>/<c>IAsyncDisposable</c>. Migrations run once at startup,
/// so this is not a hot path.
/// </remarks>
public static class RelationalMigrationRunner
{
    public static async Task RunAsync(
        Func<DbConnection> connectionFactory,
        IReadOnlyList<RelationalMigrationScript> scripts,
        RelationalMigrationDialect dialect,
        SchemaManagementMode mode,
        CancellationToken cancellationToken = default)
    {
        if (connectionFactory == null) throw new ArgumentNullException(nameof(connectionFactory));
        if (scripts == null) throw new ArgumentNullException(nameof(scripts));
        if (dialect == null) throw new ArgumentNullException(nameof(dialect));

        if (mode == SchemaManagementMode.Disabled) return;

        using var connection = connectionFactory();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await ExecuteNonQueryAsync(connection, null, dialect.EnsureTrackingTableSql, cancellationToken).ConfigureAwait(false);

        var applied = await SelectAppliedNamesAsync(connection, dialect.SelectAppliedNamesSql, cancellationToken).ConfigureAwait(false);

        foreach (var script in scripts)
        {
            if (applied.Contains(script.Name)) continue;

            if (mode == SchemaManagementMode.ValidateOnly)
                throw new InvalidOperationException(
                    $"Schema migration '{script.Name}' has not been applied and SchemaManagementMode is ValidateOnly. " +
                    "Apply migrations out-of-band or use SchemaManagementMode.ApplyMigrations.");

            using var transaction = connection.BeginTransaction();
            await ExecuteNonQueryAsync(connection, transaction, script.Sql, cancellationToken).ConfigureAwait(false);
            await ExecuteNonQueryAsync(connection, transaction, dialect.InsertAppliedNameSql, cancellationToken, script.Name)
                .ConfigureAwait(false);
            transaction.Commit();
        }
    }

    private static async Task<HashSet<string>> SelectAppliedNamesAsync(DbConnection connection, string sql,
        CancellationToken cancellationToken)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private static async Task ExecuteNonQueryAsync(DbConnection connection, DbTransaction? transaction, string sql,
        CancellationToken cancellationToken, string? nameParameter = null)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandType = CommandType.Text;
        if (transaction != null) command.Transaction = transaction;

        if (nameParameter != null)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@name";
            parameter.Value = nameParameter;
            command.Parameters.Add(parameter);
        }

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
