// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
namespace Lycia.Persistence.Relational.Internal.Migrations;

/// <summary>
/// A single named, ordered schema migration script. <see cref="Name"/> is the stable identifier recorded
/// in the migration-tracking table; it must never change once shipped.
/// </summary>
public sealed class RelationalMigrationScript
{
    public RelationalMigrationScript(string name, string sql)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Migration name must not be empty.", nameof(name));
        if (string.IsNullOrWhiteSpace(sql))
            throw new ArgumentException("Migration SQL must not be empty.", nameof(sql));

        Name = name;
        Sql = sql;
    }

    public string Name { get; }
    public string Sql { get; }
}
