// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Extensions;
using Lycia.Outbox;
using Lycia.Saga.Abstractions.Inbox;
using Lycia.Saga.Abstractions.Outbox;
using Lycia.Saga.Abstractions.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lycia.Persistence.PostgreSql;

/// <summary>
/// Contributes the PostgreSQL Inbox/Outbox providers to <see cref="LyciaPersistenceBuilder"/>, the same
/// pattern <see cref="PostgreSqlSagaStoreDslExtensions.WithPostgreSqlSagaStore"/> uses for the SagaStore.
/// </summary>
public static class PostgreSqlInboxOutboxDslExtensions
{
    /// <summary>
    /// Selects PostgreSQL as the Inbox provider, applies its schema according to
    /// <see cref="PostgreSqlInboxOptions.SchemaManagement"/>, and registers <see cref="PostgreSqlInboxStore"/>.
    /// </summary>
    public static LyciaPersistenceBuilder WithPostgreSqlInbox(
        this LyciaPersistenceBuilder persistence,
        Action<PostgreSqlInboxOptions>? configure = null)
    {
        if (persistence == null) throw new ArgumentNullException(nameof(persistence));

        var options = new PostgreSqlInboxOptions();
        configure?.Invoke(options);

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
            throw new InvalidOperationException("PostgreSqlInboxOptions.ConnectionString is required.");

        PostgreSqlInboxOutboxSchemaMigrator.RunAsync(options.ConnectionString, options.SchemaName, options.SchemaManagement)
            .GetAwaiter().GetResult();

        persistence.SelectInboxProvider("PostgreSql");

        persistence.Services.RemoveAll(typeof(IInboxStore));
        persistence.Services.AddScoped<IInboxStore>(sp => new PostgreSqlInboxStore(options,
            sp.GetService<ILyciaPersistenceSessionAccessor>()));
        persistence.RegisterProviderMetadata(PersistenceCapabilityKind.Inbox, "PostgreSql",
            PostgreSqlConnectionIdentity.Create(options.ConnectionString), true);

        return persistence;
    }

    /// <summary>
    /// Selects PostgreSQL as the Outbox provider, applies its schema according to
    /// <see cref="PostgreSqlOutboxOptions.SchemaManagement"/>, and registers <see cref="PostgreSqlOutboxStore"/>.
    /// </summary>
    public static LyciaPersistenceBuilder WithPostgreSqlOutbox(
        this LyciaPersistenceBuilder persistence,
        Action<PostgreSqlOutboxOptions>? configure = null)
    {
        if (persistence == null) throw new ArgumentNullException(nameof(persistence));

        var options = new PostgreSqlOutboxOptions();
        configure?.Invoke(options);

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
            throw new InvalidOperationException("PostgreSqlOutboxOptions.ConnectionString is required.");

        PostgreSqlInboxOutboxSchemaMigrator.RunAsync(options.ConnectionString, options.SchemaName, options.SchemaManagement)
            .GetAwaiter().GetResult();

        persistence.SelectOutboxProvider("PostgreSql");

        persistence.Services.RemoveAll(typeof(IOutboxStore));
        persistence.Services.AddScoped<IOutboxStore>(sp => new PostgreSqlOutboxStore(options,
            sp.GetService<ILyciaPersistenceSessionAccessor>()));
        persistence.RegisterProviderMetadata(PersistenceCapabilityKind.Outbox, "PostgreSql",
            PostgreSqlConnectionIdentity.Create(options.ConnectionString), true);
        return persistence.ActivateOutboxPipeline();
    }
}
