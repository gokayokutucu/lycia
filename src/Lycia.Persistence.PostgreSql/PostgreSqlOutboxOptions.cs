// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using System.Text.RegularExpressions;
using Lycia.Persistence.Relational.Internal.Migrations;
using Npgsql;

namespace Lycia.Persistence.PostgreSql;

/// <summary>Configures the PostgreSQL backed <see cref="PostgreSqlOutboxStore"/>.</summary>
public class PostgreSqlOutboxOptions
{
    private static readonly Regex ValidIdentifier = new("^[a-z_][a-z0-9_]*$", RegexOptions.Compiled);
    private string _schemaName = "public";

    /// <summary>The PostgreSQL connection string. Required.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// The database schema the outbox table lives in. Defaults to "public". Applied via the connection's
    /// <c>SearchPath</c> so the embedded schema SQL and every query can use unqualified table names.
    /// </summary>
    public string SchemaName
    {
        get => _schemaName;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || !ValidIdentifier.IsMatch(value))
                throw new ArgumentException($"'{value}' is not a valid PostgreSQL schema identifier.", nameof(value));
            _schemaName = value;
        }
    }

    /// <summary>Command timeout, in seconds, applied to every command issued by the store.</summary>
    public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>Controls whether/how the store manages its own schema at startup.</summary>
    public SchemaManagementMode SchemaManagement { get; set; } = SchemaManagementMode.ApplyMigrations;

    internal const string OutboxTable = "lycia_outbox";

    /// <summary>Builds the effective connection string, injecting <see cref="SchemaName"/> as the search path when non-default.</summary>
    internal string BuildEffectiveConnectionString()
    {
        if (SchemaName == "public") return ConnectionString;

        var builder = new NpgsqlConnectionStringBuilder(ConnectionString) { SearchPath = SchemaName };
        return builder.ConnectionString;
    }
}
