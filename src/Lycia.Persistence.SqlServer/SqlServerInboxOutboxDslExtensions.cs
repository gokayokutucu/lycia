// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Extensions;
using Lycia.Outbox;
using Lycia.Saga.Abstractions.Inbox;
using Lycia.Saga.Abstractions.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lycia.Persistence.SqlServer;

/// <summary>
/// Contributes the SQL Server Inbox/Outbox providers to <see cref="LyciaPersistenceBuilder"/>. Each is
/// independently enabled and migrates only its own schema, so a caller using only
/// <see cref="SqlServerSagaStoreDslExtensions.WithSqlServerSagaStore"/> never pays for Inbox/Outbox tables.
/// </summary>
public static class SqlServerInboxOutboxDslExtensions
{
    /// <summary>Selects SQL Server as the Inbox provider, applies its schema, and registers <see cref="SqlServerInboxStore"/>.</summary>
    public static LyciaPersistenceBuilder WithSqlServerInbox(
        this LyciaPersistenceBuilder persistence,
        Action<SqlServerInboxOptions>? configure = null)
    {
        if (persistence == null) throw new ArgumentNullException(nameof(persistence));

        var options = new SqlServerInboxOptions();
        configure?.Invoke(options);

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
            throw new InvalidOperationException("SqlServerInboxOptions.ConnectionString is required.");

        SqlServerInboxOutboxSchemaMigrator.RunAsync(options.ConnectionString, options.SchemaName, options.SchemaManagement)
            .GetAwaiter().GetResult();

        persistence.SelectInboxProvider("SqlServer");

        persistence.Services.RemoveAll(typeof(IInboxStore));
        persistence.Services.AddScoped<IInboxStore>(_ => new SqlServerInboxStore(options));

        return persistence;
    }

    /// <summary>Selects SQL Server as the Outbox provider, applies its schema, and registers <see cref="SqlServerOutboxStore"/>.</summary>
    public static LyciaPersistenceBuilder WithSqlServerOutbox(
        this LyciaPersistenceBuilder persistence,
        Action<SqlServerOutboxOptions>? configure = null)
    {
        if (persistence == null) throw new ArgumentNullException(nameof(persistence));

        var options = new SqlServerOutboxOptions();
        configure?.Invoke(options);

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
            throw new InvalidOperationException("SqlServerOutboxOptions.ConnectionString is required.");

        SqlServerInboxOutboxSchemaMigrator.RunAsync(options.ConnectionString, options.SchemaName, options.SchemaManagement)
            .GetAwaiter().GetResult();

        persistence.SelectOutboxProvider("SqlServer");

        persistence.Services.RemoveAll(typeof(IOutboxStore));
        persistence.Services.AddScoped<IOutboxStore>(_ => new SqlServerOutboxStore(options));
        persistence.Services.TryAddScoped<IOutboxDispatcher, OutboxDispatcher>();

        return persistence;
    }
}
