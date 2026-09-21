// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Microsoft.Data.SqlClient;

namespace Lycia.Persistence.SqlServer;

internal static class SqlServerConnectionIdentity
{
    public static string Create(string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        return $"{builder.DataSource.Trim().ToLowerInvariant()}/{builder.InitialCatalog.Trim().ToLowerInvariant()}";
    }
}
