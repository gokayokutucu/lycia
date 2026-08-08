// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
namespace Lycia.Persistence.Relational.Internal.Migrations;

/// <summary>
/// The small set of dialect-specific SQL statements <see cref="RelationalMigrationRunner"/> needs to manage
/// its own migration-tracking table. Everything else about running migrations is driver-agnostic; only the
/// tracking-table DDL/DML differs between SQL Server and PostgreSQL, so each provider package supplies its
/// own dialect here rather than the internal project taking a dependency on either driver.
/// </summary>
public sealed class RelationalMigrationDialect
{
    public RelationalMigrationDialect(string ensureTrackingTableSql, string selectAppliedNamesSql, string insertAppliedNameSql)
    {
        EnsureTrackingTableSql = ensureTrackingTableSql;
        SelectAppliedNamesSql = selectAppliedNamesSql;
        InsertAppliedNameSql = insertAppliedNameSql;
    }

    /// <summary>DDL that creates the migration-tracking table if it does not already exist.</summary>
    public string EnsureTrackingTableSql { get; }

    /// <summary>Selects the names of migrations already recorded as applied.</summary>
    public string SelectAppliedNamesSql { get; }

    /// <summary>Records a migration as applied. Must accept a single named parameter, <c>@name</c>.</summary>
    public string InsertAppliedNameSql { get; }
}
