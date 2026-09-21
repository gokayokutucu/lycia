// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Extensions;
using Lycia.Persistence.Relational.Internal.Sessions;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Persistence;
using Lycia.Saga.Abstractions.Outbox;
using Lycia.Saga.Abstractions.Persistence.Journal;
using Lycia.Saga.Abstractions.Persistence.Reconciliation;
using Lycia.Saga.Abstractions.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Lycia.Persistence.PostgreSql;

/// <summary>
/// Contributes the PostgreSQL SagaStore provider to <see cref="LyciaPersistenceBuilder"/>. Lycia.Extensions
/// defines the builder; this package only adds a provider method to it, so Lycia.Extensions never depends
/// on Lycia.Persistence.PostgreSql.
/// </summary>
public static class PostgreSqlSagaStoreDslExtensions
{
    /// <summary>Selects PostgreSQL as the relational canonical side of an explicit Split Store.</summary>
    public static LyciaPersistenceBuilder WithPostgreSqlCanonicalSagaStore(
        this LyciaPersistenceBuilder persistence,
        Action<PostgreSqlSagaStoreOptions>? configure = null)
    {
        WithPostgreSqlSagaStore(persistence, configure);
        var options = persistence.Services.Last(x => x.ServiceType == typeof(PostgreSqlSagaStoreOptions))
            .ImplementationInstance as PostgreSqlSagaStoreOptions;
        if (options == null)
            throw new InvalidOperationException("PostgreSQL canonical options were not registered.");
        PostgreSqlReconciliationSchemaMigrator.RunAsync(options).GetAwaiter().GetResult();
        persistence.Services.RemoveAll(typeof(IReconciliationStore));
        persistence.Services.AddScoped<IReconciliationStore>(sp => new PostgreSqlReconciliationStore(options,
            sp.GetService<ILyciaPersistenceSessionAccessor>()));
        PostgreSqlJournalSchemaMigrator.RunAsync(options).GetAwaiter().GetResult();
        persistence.Services.RemoveAll(typeof(ISagaJournalStore));
        persistence.Services.AddScoped<ISagaJournalStore>(sp => new PostgreSqlSagaJournalStore(options,
            sp.GetService<ILyciaPersistenceSessionAccessor>()));
        var identity = PostgreSqlConnectionIdentity.Create(options.ConnectionString);
        persistence.SelectSplitStoreCanonicalProvider("PostgreSql", identity);
        persistence.RegisterProviderMetadata(PersistenceCapabilityKind.Reconciliation, "PostgreSql", identity, true);
        persistence.RegisterProviderMetadata(PersistenceCapabilityKind.Journal, "PostgreSql", identity, true);
        return persistence;
    }

    /// <summary>
    /// Selects PostgreSQL as the SagaStore provider, applies its schema according to
    /// <see cref="PostgreSqlSagaStoreOptions.SchemaManagement"/>, and registers <see cref="PostgreSqlSagaStore"/>.
    /// </summary>
    public static LyciaPersistenceBuilder WithPostgreSqlSagaStore(
        this LyciaPersistenceBuilder persistence,
        Action<PostgreSqlSagaStoreOptions>? configure = null)
    {
        if (persistence == null) throw new ArgumentNullException(nameof(persistence));

        persistence.SelectProvider("PostgreSql");

        var options = new PostgreSqlSagaStoreOptions();
        configure?.Invoke(options);

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
            throw new InvalidOperationException("PostgreSqlSagaStoreOptions.ConnectionString is required.");

        PostgreSqlSchemaMigrator.RunAsync(options).GetAwaiter().GetResult();
        persistence.Services.RemoveAll(typeof(PostgreSqlSagaStoreOptions));
        persistence.Services.AddSingleton(options);

        persistence.Services.RemoveAll(typeof(ISagaStore));
        persistence.Services.AddScoped<ISagaStore>(sp => new PostgreSqlSagaStore(
            options,
            sp.GetRequiredService<IEventBus>(),
            sp.GetRequiredService<ISagaIdGenerator>(),
            sp.GetRequiredService<ISagaCompensationCoordinator>(),
            sp.GetService<IMessageScheduler>(),
            sp.GetService<IOutgoingMessagePipeline>(),
            sp.GetService<ILyciaPersistenceSessionAccessor>()));

        persistence.Services.RemoveAll(typeof(ILyciaPersistenceSessionFactory));
        persistence.Services.AddScoped<ILyciaPersistenceSessionFactory>(_ =>
            new RelationalPersistenceSessionFactory(() => new NpgsqlConnection(options.BuildEffectiveConnectionString())));
        persistence.RegisterProviderMetadata(PersistenceCapabilityKind.SagaStore, "PostgreSql",
            PostgreSqlConnectionIdentity.Create(options.ConnectionString), true);

        return persistence;
    }
}
