// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using System.Text.RegularExpressions;
using Lycia.Persistence.Relational.Internal.Migrations;

namespace Lycia.Persistence.SqlServer;

/// <summary>Configures the SQL Server backed <see cref="SqlServerSagaStore"/>.</summary>
public class SqlServerSagaStoreOptions
{
    private static readonly Regex ValidIdentifier = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);
    private string _schemaName = "dbo";

    /// <summary>The SQL Server connection string. Required.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>The database schema the saga tables live in. Defaults to "dbo".</summary>
    public string SchemaName
    {
        get => _schemaName;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || !ValidIdentifier.IsMatch(value))
                throw new ArgumentException($"'{value}' is not a valid SQL Server schema identifier.", nameof(value));
            _schemaName = value;
        }
    }

    /// <summary>Command timeout, in seconds, applied to every command issued by the store.</summary>
    public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>Controls whether/how the store manages its own schema at startup.</summary>
    public SchemaManagementMode SchemaManagement { get; set; } = SchemaManagementMode.ApplyMigrations;

    internal string SagaDataTable => $"{SchemaName}.LyciaSagaData";
    internal string SagaStepsTable => $"{SchemaName}.LyciaSagaSteps";
}
