// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Extensions;
using Lycia.Persistence.Relational.Internal.Sessions;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Persistence;
using Lycia.Saga.Abstractions.Outbox;
using Lycia.Saga.Abstractions.Persistence.Reconciliation;
using Lycia.Saga.Abstractions.Scheduling;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lycia.Persistence.SqlServer;

/// <summary>
/// Contributes the SQL Server SagaStore provider to <see cref="LyciaPersistenceBuilder"/>. Lycia.Extensions
/// defines the builder; this package only adds a provider method to it, so Lycia.Extensions never depends
/// on Lycia.Persistence.SqlServer.
/// </summary>
public static class SqlServerSagaStoreDslExtensions
{
    /// <summary>Selects SQL Server as the relational canonical side of an explicit Split Store.</summary>
    public static LyciaPersistenceBuilder WithSqlServerCanonicalSagaStore(this LyciaPersistenceBuilder persistence,
        Action<SqlServerSagaStoreOptions>? configure = null)
    {
        WithSqlServerSagaStore(persistence, configure);
        var options = persistence.Services.Last(x => x.ServiceType == typeof(SqlServerSagaStoreOptions)).ImplementationInstance as SqlServerSagaStoreOptions
            ?? throw new InvalidOperationException("SQL Server canonical options were not registered.");
        SqlServerReconciliationSchemaMigrator.RunAsync(options).GetAwaiter().GetResult();
        persistence.Services.RemoveAll(typeof(IReconciliationStore));
        persistence.Services.AddScoped<IReconciliationStore>(sp => new SqlServerReconciliationStore(options,sp.GetService<ILyciaPersistenceSessionAccessor>()));
        var identity=SqlServerConnectionIdentity.Create(options.ConnectionString);
        persistence.SelectSplitStoreCanonicalProvider("SqlServer",identity);
        persistence.RegisterProviderMetadata(PersistenceCapabilityKind.Reconciliation,"SqlServer",identity,true);
        return persistence;
    }
    /// <summary>
    /// Selects SQL Server as the SagaStore provider, applies its schema according to
    /// <see cref="SqlServerSagaStoreOptions.SchemaManagement"/>, and registers <see cref="SqlServerSagaStore"/>.
    /// </summary>
    public static LyciaPersistenceBuilder WithSqlServerSagaStore(
        this LyciaPersistenceBuilder persistence,
        Action<SqlServerSagaStoreOptions>? configure = null)
    {
        if (persistence == null) throw new ArgumentNullException(nameof(persistence));

        persistence.SelectProvider("SqlServer");

        var options = new SqlServerSagaStoreOptions();
        configure?.Invoke(options);

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
            throw new InvalidOperationException("SqlServerSagaStoreOptions.ConnectionString is required.");

        SqlServerSchemaMigrator.RunAsync(options).GetAwaiter().GetResult();
        persistence.Services.RemoveAll(typeof(SqlServerSagaStoreOptions));
        persistence.Services.AddSingleton(options);

        persistence.Services.RemoveAll(typeof(ISagaStore));
        persistence.Services.AddScoped<ISagaStore>(sp => new SqlServerSagaStore(
            options,
            sp.GetRequiredService<IEventBus>(),
            sp.GetRequiredService<ISagaIdGenerator>(),
            sp.GetRequiredService<ISagaCompensationCoordinator>(),
            sp.GetService<IMessageScheduler>(),
            sp.GetService<IOutgoingMessagePipeline>(),
            sp.GetService<ILyciaPersistenceSessionAccessor>()));

        persistence.Services.RemoveAll(typeof(ILyciaPersistenceSessionFactory));
        persistence.Services.AddScoped<ILyciaPersistenceSessionFactory>(_ =>
            new RelationalPersistenceSessionFactory(() => new SqlConnection(options.ConnectionString)));
        persistence.RegisterProviderMetadata(PersistenceCapabilityKind.SagaStore, "SqlServer",
            SqlServerConnectionIdentity.Create(options.ConnectionString), true);

        return persistence;
    }
}
