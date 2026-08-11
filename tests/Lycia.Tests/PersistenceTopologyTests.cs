using Lycia.Extensions;
using Lycia.Saga.Abstractions.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lycia.Tests;

public class PersistenceTopologyTests
{
    [Theory]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    public void Auto_selects_local_atomic_for_same_relational_database(string provider)
    {
        var topology = Resolve(builder =>
        {
            RegisterAll(builder, provider, "db/orders");
        });

        Assert.Equal(PersistenceBoundaryPolicy.Auto, topology.BoundaryPolicy);
        Assert.Equal(PersistenceExecutionStrategy.LocalAtomic, topology.ResolvedStrategy);
    }

    [Theory]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    public void Auto_selects_independent_for_different_databases(string provider)
    {
        var topology = Resolve(builder =>
        {
            builder.RegisterProviderMetadata(PersistenceCapabilityKind.SagaStore, provider, "db/orders", true);
            builder.RegisterProviderMetadata(PersistenceCapabilityKind.Inbox, provider, "db/orders", true);
            builder.RegisterProviderMetadata(PersistenceCapabilityKind.Outbox, provider, "db/payments", true);
        });

        Assert.Equal(PersistenceExecutionStrategy.Independent, topology.ResolvedStrategy);
    }

    [Fact]
    public void Auto_selects_independent_for_mixed_providers()
    {
        var topology = Resolve(builder =>
        {
            builder.RegisterProviderMetadata(PersistenceCapabilityKind.SagaStore, "Redis", null, false);
            builder.RegisterProviderMetadata(PersistenceCapabilityKind.Inbox, "PostgreSql", "db/orders", true);
            builder.RegisterProviderMetadata(PersistenceCapabilityKind.Outbox, "PostgreSql", "db/orders", true);
        });

        Assert.Equal(PersistenceExecutionStrategy.Independent, topology.ResolvedStrategy);
    }

    [Fact]
    public void Require_atomic_accepts_compatible_topology()
    {
        var topology = Resolve(builder =>
        {
            RegisterAll(builder, "PostgreSql", "db/orders");
            builder.RequireAtomicBoundary();
        });

        Assert.Equal(PersistenceExecutionStrategy.LocalAtomic, topology.ResolvedStrategy);
    }

    [Fact]
    public void Require_atomic_rejects_mixed_topology_without_exposing_secrets()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Resolve(builder =>
        {
            builder.RegisterProviderMetadata(PersistenceCapabilityKind.SagaStore, "Redis", null, false);
            builder.RegisterProviderMetadata(PersistenceCapabilityKind.Inbox, "PostgreSql", "db/orders", true);
            builder.RequireAtomicBoundary();
        }));

        Assert.Contains("do not share a compatible transaction boundary", exception.Message);
        Assert.DoesNotContain("Password", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Force_independent_overrides_available_atomic_boundary()
    {
        var topology = Resolve(builder =>
        {
            RegisterAll(builder, "PostgreSql", "db/orders");
            builder.UseIndependentTransactions();
        });

        Assert.Equal(PersistenceExecutionStrategy.Independent, topology.ResolvedStrategy);
    }

    [Fact]
    public void Conflicting_explicit_policies_fail_clearly()
    {
        var builder = CreateBuilder(out _);
        builder.RequireAtomicBoundary();

        var exception = Assert.Throws<InvalidOperationException>(() => builder.UseIndependentTransactions());
        Assert.Contains("Conflicting persistence boundary policies", exception.Message);
    }

    private static PersistenceTopology Resolve(Action<LyciaPersistenceBuilder> configure)
    {
        var builder = CreateBuilder(out var services);
        configure(builder);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IPersistenceTopology>().Current;
    }

    private static LyciaPersistenceBuilder CreateBuilder(out ServiceCollection services)
    {
        services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ApplicationId"] = "topology-tests" })
            .Build();
        return services.AddLycia(configuration).UsePersistence();
    }

    private static void RegisterAll(LyciaPersistenceBuilder builder, string provider, string identity)
    {
        builder.RegisterProviderMetadata(PersistenceCapabilityKind.SagaStore, provider, identity, true);
        builder.RegisterProviderMetadata(PersistenceCapabilityKind.Inbox, provider, identity, true);
        builder.RegisterProviderMetadata(PersistenceCapabilityKind.Outbox, provider, identity, true);
    }
}
