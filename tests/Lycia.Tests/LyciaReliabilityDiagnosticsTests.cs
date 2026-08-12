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
        Assert.False(snapshot.OutboxEnabled);
    }
}
