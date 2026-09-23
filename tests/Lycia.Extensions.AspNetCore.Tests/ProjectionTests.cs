// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
using Lycia.Saga.Abstractions.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;

namespace Lycia.Extensions.AspNetCore.Tests;

/// <summary>
/// Proves the endpoint is a thin, faithful projection of <see cref="ILyciaReliabilityDiagnostics"/>: it
/// reports exactly what the snapshot says, for every representative persistence strategy and provider,
/// and it never infers topology on its own.
/// </summary>
public class ProjectionTests
{
    private static async Task<(JObject Body, StubDiagnostics Stub)> GetAsync(LyciaReliabilitySnapshot snapshot)
    {
        var stub = new StubDiagnostics(snapshot);
        using var host = await TestHost.StartAsync(
            services => services.AddSingleton<ILyciaReliabilityDiagnostics>(stub),
            app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints => endpoints.MapLyciaDiagnostics());
            });

        var response = await host.Client().GetAsync("/diagnostics/lycia");
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());
        return (body, stub);
    }

    [Fact]
    public async Task Response_Reports_AtLeastOnce_Delivery_Guarantee()
    {
        var (body, _) = await GetAsync(new LyciaReliabilitySnapshot());

        Assert.Equal("AtLeastOnce", (string?)body["deliveryGuarantee"]);
    }

    [Fact]
    public async Task Response_Is_Produced_From_The_Diagnostics_Service_Not_Reconstructed_Independently()
    {
        var (_, stub) = await GetAsync(new LyciaReliabilitySnapshot { Mode = PersistenceMode.SplitStore });

        Assert.Equal(1, stub.CallCount);
    }

    [Theory]
    [InlineData("Redis")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    [InlineData("InMemory")]
    public async Task Standard_Mode_Reports_The_Registered_SagaStore_Provider(string provider)
    {
        var (body, _) = await GetAsync(new LyciaReliabilitySnapshot
        {
            Mode = PersistenceMode.Standard,
            SagaStoreProvider = provider,
            ResolvedStrategy = PersistenceExecutionStrategy.Independent
        });

        Assert.Equal("Standard", (string?)body["persistence"]!["mode"]);
        Assert.Equal(provider, (string?)body["persistence"]!["provider"]);
        Assert.Equal("Independent", (string?)body["persistence"]!["resolvedStrategy"]);
        Assert.Equal(JTokenType.Null, body["persistence"]!["canonicalStore"]!.Type);
    }

    [Fact]
    public async Task Independent_Strategy_Is_Reported_As_Independent()
    {
        var (body, _) = await GetAsync(new LyciaReliabilitySnapshot
        {
            Mode = PersistenceMode.Standard,
            SagaStoreProvider = "SqlServer",
            ResolvedStrategy = PersistenceExecutionStrategy.Independent
        });

        Assert.Equal("Independent", (string?)body["persistence"]!["resolvedStrategy"]);
    }

    [Fact]
    public async Task LocalAtomic_Strategy_Is_Reported_As_LocalAtomic()
    {
        var (body, _) = await GetAsync(new LyciaReliabilitySnapshot
        {
            Mode = PersistenceMode.Standard,
            SagaStoreProvider = "PostgreSql",
            ResolvedStrategy = PersistenceExecutionStrategy.LocalAtomic
        });

        Assert.Equal("LocalAtomic", (string?)body["persistence"]!["resolvedStrategy"]);
    }

    [Fact]
    public async Task Split_Store_Reports_Canonical_And_Operational_Stores_Without_A_Standalone_Provider()
    {
        var (body, _) = await GetAsync(new LyciaReliabilitySnapshot
        {
            Mode = PersistenceMode.SplitStore,
            ResolvedStrategy = PersistenceExecutionStrategy.LocalAtomic,
            CanonicalStore = "SqlServer",
            OperationalStore = "Redis",
            ReconciliationEnabled = true
        });

        Assert.Equal("SplitStore", (string?)body["persistence"]!["mode"]);
        Assert.Equal("SqlServer", (string?)body["persistence"]!["canonicalStore"]);
        Assert.Equal("Redis", (string?)body["persistence"]!["operationalStore"]);
        Assert.Equal(JTokenType.Null, body["persistence"]!["provider"]!.Type);
        Assert.True((bool?)body["capabilities"]!["reconciliation"]);
    }

    [Fact]
    public async Task Capabilities_Reflect_Every_Flag_On_The_Snapshot()
    {
        var (body, _) = await GetAsync(new LyciaReliabilitySnapshot
        {
            InboxEnabled = true,
            OutboxEnabled = true,
            JournalEnabled = true,
            JournalRebuildAvailable = true,
            ReconciliationEnabled = true
        });

        Assert.True((bool?)body["capabilities"]!["inbox"]);
        Assert.True((bool?)body["capabilities"]!["outbox"]);
        Assert.True((bool?)body["capabilities"]!["journal"]);
        Assert.True((bool?)body["capabilities"]!["journalRebuild"]);
        Assert.True((bool?)body["capabilities"]!["reconciliation"]);
    }

    [Fact]
    public async Task Capabilities_Are_False_When_Nothing_Is_Registered()
    {
        var (body, _) = await GetAsync(new LyciaReliabilitySnapshot());

        Assert.False((bool?)body["capabilities"]!["inbox"]);
        Assert.False((bool?)body["capabilities"]!["outbox"]);
        Assert.False((bool?)body["capabilities"]!["journal"]);
        Assert.False((bool?)body["capabilities"]!["journalRebuild"]);
        Assert.False((bool?)body["capabilities"]!["reconciliation"]);
    }
}
