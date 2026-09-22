// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
using Lycia.Extensions;
using Lycia.Persistence.Redis;
using Lycia.Saga.Abstractions.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lycia.Tests;

public class LyciaReliabilityDiagnosticsTests
{
    private static IConfiguration Configuration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ApplicationId"] = "DiagnosticsTestApp" })
            .Build();

    [Fact]
    public void GetSnapshot_Without_Persistence_Configured_Reports_Safe_Defaults()
    {
        var services = new ServiceCollection();
        services.AddLycia(Configuration());

        var provider = services.BuildServiceProvider();
        var diagnostics = provider.GetRequiredService<ILyciaReliabilityDiagnostics>();

        var snapshot = diagnostics.GetSnapshot();

        Assert.Equal("AtLeastOnce", snapshot.DeliveryGuarantee);
        Assert.Null(snapshot.SagaStoreProvider);
        Assert.False(snapshot.JournalEnabled);
        Assert.False(snapshot.JournalRebuildAvailable);
        Assert.False(snapshot.OutboxEnabled);
    }

    [Fact]
    public void GetSnapshot_Reflects_Registered_SagaStore_Provider()
    {
        var services = new ServiceCollection();
        var builder = services.AddLycia(Configuration());
        builder.UsePersistence().WithRedisSagaStore();

        var provider = services.BuildServiceProvider();
        var diagnostics = provider.GetRequiredService<ILyciaReliabilityDiagnostics>();

        var snapshot = diagnostics.GetSnapshot();

        Assert.Equal(PersistenceMode.Standard, snapshot.Mode);
        Assert.Equal("Redis", snapshot.SagaStoreProvider);
        Assert.Null(snapshot.CanonicalStore);
        Assert.Null(snapshot.OperationalStore);
        Assert.False(snapshot.OutboxEnabled);
    }

    [Fact]
    public void GetSnapshot_Reports_SplitStore_Topology_Without_A_Standalone_SagaStoreProvider()
    {
        // Split Store's own topology is fully covered by real-infrastructure provider-suite tests
        // (WithPostgreSqlCanonicalSagaStore/WithSqlServerCanonicalSagaStore migrate schema eagerly and
        // therefore need a live database). This test only proves GetSnapshot() projects a resolved
        // IPersistenceTopology faithfully, without inferring topology itself, so it substitutes a
        // hand-built topology instead of exercising the real DSL/migration path.
        var services = new ServiceCollection();
        services.AddLycia(Configuration());
        services.AddSingleton<IPersistenceTopology>(new FixedPersistenceTopology(new PersistenceTopology(
            PersistenceBoundaryPolicy.RequireAtomic,
            PersistenceExecutionStrategy.LocalAtomic,
            stores:
            [
                new PersistenceStoreDescriptor(PersistenceCapabilityKind.SagaStore, "PostgreSql", "db/lycia", true),
                new PersistenceStoreDescriptor(PersistenceCapabilityKind.Inbox, "PostgreSql", "db/lycia", true),
                new PersistenceStoreDescriptor(PersistenceCapabilityKind.Outbox, "PostgreSql", "db/lycia", true)
            ],
            reason: "test",
            mode: PersistenceMode.SplitStore,
            canonicalStore: "PostgreSql",
            operationalStore: "Redis",
            reconciliationEnabled: true)));

        var provider = services.BuildServiceProvider();
        var diagnostics = provider.GetRequiredService<ILyciaReliabilityDiagnostics>();

        var snapshot = diagnostics.GetSnapshot();

        Assert.Equal(PersistenceMode.SplitStore, snapshot.Mode);
        Assert.Null(snapshot.SagaStoreProvider);
        Assert.Equal("PostgreSql", snapshot.CanonicalStore);
        Assert.Equal("Redis", snapshot.OperationalStore);
        Assert.Equal(PersistenceExecutionStrategy.LocalAtomic, snapshot.ResolvedStrategy);
        Assert.True(snapshot.ReconciliationEnabled);
    }

    [Fact]
    public void GetSnapshot_Never_Opens_A_Real_Connection_When_Redis_Inbox_And_Outbox_Are_Registered()
    {
        // RedisInboxOutboxDslExtensions registers IInboxStore/IOutboxStore factories that resolve
        // IConnectionMultiplexer, whose registration calls StackExchange.Redis's ConnectionMultiplexer
        // .Connect(...) synchronously on first resolution. GetSnapshot() must stay a pure configuration
        // read, so it must never invoke those factories - only check whether they are registered. An
        // unroutable address makes any real connection attempt hang/fail slowly; GetSnapshot() completing
        // well within a tight budget is the proof no such attempt was made.
        var services = new ServiceCollection();
        var builder = services.AddLycia(Configuration());
        builder.UsePersistence()
            .WithRedisSagaStore(o => o.ConnectionString = "10.255.255.1:6379,connectTimeout=100,abortConnect=false")
            .WithRedisInbox(o => o.ConnectionString = "10.255.255.1:6379,connectTimeout=100,abortConnect=false")
            .WithRedisOutbox(o => o.ConnectionString = "10.255.255.1:6379,connectTimeout=100,abortConnect=false");

        var provider = services.BuildServiceProvider();
        var diagnostics = provider.GetRequiredService<ILyciaReliabilityDiagnostics>();

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var snapshot = diagnostics.GetSnapshot();
        stopwatch.Stop();

        Assert.True(snapshot.InboxEnabled);
        Assert.True(snapshot.OutboxEnabled);
        Assert.True(stopwatch.ElapsedMilliseconds < 2000,
            $"GetSnapshot() took {stopwatch.ElapsedMilliseconds} ms against an unroutable address - it likely tried to connect.");
    }

    private sealed class FixedPersistenceTopology(PersistenceTopology topology) : IPersistenceTopology
    {
        public PersistenceTopology Current => topology;
    }
}
