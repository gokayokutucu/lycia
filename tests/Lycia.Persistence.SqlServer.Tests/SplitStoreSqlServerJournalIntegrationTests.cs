// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Common.Messaging;
using Lycia.Extensions;
using Lycia.Extensions.Journal;
using Lycia.Persistence.Redis;
using Lycia.Persistence.Relational.Internal.Migrations;
using Lycia.Persistence.TestKit;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Messaging;
using Lycia.Saga.Abstractions.Persistence.Journal;
using Lycia.Saga.Abstractions.Persistence.Reconciliation;
using Lycia.Saga.Exceptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lycia.Persistence.SqlServer.Tests;

[Collection(SqlServerJournalIntegrationCollection.Name)]
public class SplitStoreSqlServerJournalIntegrationTests(SqlServerJournalIntegrationFixture fixture)
{
    [Fact]
    public async Task Split_store_DSL_wiring_appends_journal_entries_through_the_resolved_ISagaStore()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var sagaStore = scope.ServiceProvider.GetRequiredService<ISagaStore>();
        var journalStore = scope.ServiceProvider.GetRequiredService<ISagaJournalStore>();

        var sagaId = Guid.NewGuid();
        var data = new DummySagaData { Payload = "v1" };
        await sagaStore.SaveSagaDataAsync(sagaId, data);
        var loaded = await sagaStore.LoadSagaDataAsync<DummySagaData>(sagaId);
        loaded.Payload = "v2";
        await sagaStore.SaveSagaDataAsync(sagaId, loaded);
        var loaded2 = await sagaStore.LoadSagaDataAsync<DummySagaData>(sagaId);
        loaded2.Payload = "v3";
        await sagaStore.SaveSagaDataAsync(sagaId, loaded2);

        var entries = await journalStore.ReadAsync(sagaId, 0, 10);
        Assert.Equal(3, entries.Count);
        Assert.Equal([1L, 2L, 3L], entries.Select(e => e.SequenceNumber).ToArray());
        Assert.Equal(3, await journalStore.GetLatestVersionAsync(sagaId));
    }

    [Fact]
    public async Task Rebuild_service_restores_the_Redis_operational_projection_from_the_canonical_journal()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var sagaStore = scope.ServiceProvider.GetRequiredService<ISagaStore>();
        var operationalStore = scope.ServiceProvider.GetRequiredService<IOperationalSagaProjectionStore>();
        var journalStore = scope.ServiceProvider.GetRequiredService<ISagaJournalStore>();

        var sagaId = Guid.NewGuid();
        var data = new DummySagaData { Payload = "v1" };
        await sagaStore.SaveSagaDataAsync(sagaId, data);
        var loaded = await sagaStore.LoadSagaDataAsync<DummySagaData>(sagaId);
        loaded.Payload = "v2";
        await sagaStore.SaveSagaDataAsync(sagaId, loaded);
        var loaded2 = await sagaStore.LoadSagaDataAsync<DummySagaData>(sagaId);
        loaded2.Payload = "v3";
        await sagaStore.SaveSagaDataAsync(sagaId, loaded2);

        var lastVersion = await journalStore.GetLatestVersionAsync(sagaId);
        Assert.Equal(3, lastVersion);

        // Simulate loss of the rebuildable operational projection.
        await operationalStore.DeleteAsync(sagaId);
        Assert.Equal(0, await operationalStore.GetVersionAsync(sagaId));

        var rebuildService = new SagaRebuildService(
            journalStore,
            new SagaJournalReducer(),
            operationalStore,
            CreateCanonicalStore(),
            new JournalEntryUpcastChain([]));

        var outcome = await rebuildService.RebuildSagaAsync(sagaId);

        Assert.True(outcome.Succeeded);
        Assert.Equal(lastVersion, await operationalStore.GetVersionAsync(sagaId));
    }

    [Fact]
    public async Task Concurrent_saves_at_the_same_expected_version_produce_exactly_one_winner_and_one_journal_entry()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var sagaStore = (IVersionedSagaStore)scope.ServiceProvider.GetRequiredService<ISagaStore>();
        var journalStore = scope.ServiceProvider.GetRequiredService<ISagaJournalStore>();
        var sagaId = Guid.NewGuid();

        var first = Task.Run(() => sagaStore.SaveSagaDataAsync(sagaId, new DummySagaData { Payload = "a" }, 0));
        var second = Task.Run(() => sagaStore.SaveSagaDataAsync(sagaId, new DummySagaData { Payload = "b" }, 0));

        var results = await Task.WhenAll(
            Wrap(first),
            Wrap(second));

        Assert.Single(results, r => r.Succeeded);
        Assert.Single(results, r => !r.Succeeded && r.Exception is SagaConcurrencyException);

        var entries = await journalStore.ReadAsync(sagaId, 0, 10);
        Assert.Single(entries);
        Assert.Equal(1, entries[0].TargetVersion);
    }

    private static async Task<(bool Succeeded, Exception? Exception)> Wrap(Task<long> task)
    {
        try
        {
            await task;
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex);
        }
    }

    private ISagaStore CreateCanonicalStore()
    {
        // The rebuild service reads the canonical relational store directly, the same way
        // SagaRebuildService is constructed by the DSL's internal Split Store composition.
        var options = new SqlServerSagaStoreOptions
        {
            ConnectionString = fixture.SqlConnectionString,
            SchemaManagement = SchemaManagementMode.ApplyMigrations
        };
        return new SqlServerSagaStore(options, null!, null!, null!, null);
    }

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>("ApplicationId", "sqlserver-journal-tests")])
            .Build();
        var builder = services.AddLycia(configuration);
        services.AddSingleton<IEventBus, NoopEventBus>();

        builder.UsePersistence()
            .WithSqlServerCanonicalSagaStore(o =>
            {
                o.ConnectionString = fixture.SqlConnectionString;
                o.SchemaManagement = SchemaManagementMode.ApplyMigrations;
            })
            .WithRedisOperationalSagaStore(o => o.ConnectionString = fixture.RedisConnectionString)
            .UseSplitStore();
        builder.Build();

        return services.BuildServiceProvider();
    }

    private sealed class NoopEventBus : IEventBus
    {
        public string ApplicationId => "sqlserver-journal-tests";

        public Task Send<TCommand>(TCommand command, Type? handlerType = null, Guid? sagaId = null,
            CancellationToken cancellationToken = default) where TCommand : ICommand => Task.CompletedTask;

        public Task Respond<TRequest, TResponse>(TRequest request, TResponse response, Type? handlerType = null,
            Guid? sagaId = null, CancellationToken cancellationToken = default)
            where TRequest : IMessage where TResponse : IResponse<TRequest> => Task.CompletedTask;

        public Task Publish<TEvent>(TEvent @event, Type? handlerType = null, Guid? sagaId = null,
            CancellationToken cancellationToken = default) where TEvent : IEvent => Task.CompletedTask;

        public IAsyncEnumerable<(byte[] Body, Type MessageType, Type HandlerType, IReadOnlyDictionary<string, object?> Headers)>
            ConsumeAsync(bool autoAck = true, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by these tests.");

        public IAsyncEnumerable<IncomingMessage> ConsumeWithAckAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by these tests.");
    }
}
