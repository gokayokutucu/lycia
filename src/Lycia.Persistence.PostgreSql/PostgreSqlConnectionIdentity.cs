// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Npgsql;

namespace Lycia.Persistence.PostgreSql;

internal static class PostgreSqlConnectionIdentity
{
    public static string Create(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var database = string.IsNullOrWhiteSpace(builder.Database) ? builder.Username : builder.Database;
        return $"{(builder.Host ?? string.Empty).Trim().ToLowerInvariant()}:{builder.Port}/" +
               $"{(database ?? string.Empty).Trim().ToLowerInvariant()}";
    }
}
