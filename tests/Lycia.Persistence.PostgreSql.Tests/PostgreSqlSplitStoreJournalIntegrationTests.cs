// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
using Lycia.Extensions;
using Lycia.Extensions.Journal;
using Lycia.Persistence.Redis;
using Lycia.Persistence.TestKit;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Persistence;
using Lycia.Saga.Abstractions.Persistence.Journal;
using Lycia.Saga.Abstractions.Persistence.Reconciliation;
using Lycia.Saga.Exceptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lycia.Persistence.PostgreSql.Tests;

/// <summary>
/// End-to-end proof that the PostgreSQL journal store wires correctly through the public
/// <see cref="LyciaPersistenceBuilder"/> DSL for Split Store: canonical PostgreSQL + operational
/// Redis + the immutable journal, using real Testcontainers-backed PostgreSQL and Redis.
/// </summary>
[Collection("PostgreSqlContainer")]
public class PostgreSqlSplitStoreJournalIntegrationTests(PostgreSqlContainerFixture pgFixture, RedisContainerFixture redisFixture)
    : IClassFixture<RedisContainerFixture>
{
    [Fact]
    public async Task DSL_wiring_saves_saga_data_and_records_journal_entries_with_correct_sequence()
    {
        await using var provider = BuildSplitStoreProvider($"journal-dsl-{Guid.NewGuid():N}");
        var sagaId = Guid.NewGuid();

        await using (var scope = provider.CreateAsyncScope())
        {
            var sagaStore = scope.ServiceProvider.GetRequiredService<ISagaStore>();
            for (var i = 0; i < 3; i++)
            {
                var data = new DummySagaData { Counter = i };
                await sagaStore.SaveSagaDataAsync(sagaId, data);
            }
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var journalStore = scope.ServiceProvider.GetRequiredService<ISagaJournalStore>();
            var entries = await journalStore.ReadAsync(sagaId, 0, 10);
            Assert.Equal(3, entries.Count);
            Assert.Equal([1L, 2L, 3L], entries.Select(x => x.TargetVersion).ToArray());
            Assert.Equal(3, await journalStore.GetLatestVersionAsync(sagaId));
        }
    }

    [Fact]
    public async Task Rebuild_service_restores_redis_projection_from_journal_history()
    {
        await using var provider = BuildSplitStoreProvider($"journal-rebuild-{Guid.NewGuid():N}");
        var sagaId = Guid.NewGuid();

        await using var scope = provider.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var sagaStore = sp.GetRequiredService<ISagaStore>();

        for (var i = 0; i < 3; i++)
            await sagaStore.SaveSagaDataAsync(sagaId, new DummySagaData { Counter = i });

        // The Redis operational projection is only materialized asynchronously by the reconciliation
        // worker (not running in this test), so it starts empty here. Deleting it first keeps the proof
        // focused on what matters: rebuild must restore the projection purely from canonical journal
        // history, independent of whatever operational state (or lack of it) existed beforehand.
        var operationalStore = sp.GetRequiredService<IOperationalSagaProjectionStore>();
        await operationalStore.DeleteAsync(sagaId);
        Assert.Equal(0, await operationalStore.GetVersionAsync(sagaId));

        var journalStore = sp.GetRequiredService<ISagaJournalStore>();
        var canonicalOptions = sp.GetRequiredService<PostgreSqlSagaStoreOptions>();
        var canonicalStore = new PostgreSqlSagaStore(
            canonicalOptions,
            sp.GetRequiredService<IEventBus>(),
            sp.GetRequiredService<ISagaIdGenerator>(),
            sp.GetRequiredService<ISagaCompensationCoordinator>());

        var rebuildService = new SagaRebuildService(
            journalStore, new SagaJournalReducer(), operationalStore, canonicalStore,
            new JournalEntryUpcastChain([]));

        var outcome = await rebuildService.RebuildSagaAsync(sagaId);

        Assert.True(outcome.Succeeded);
        Assert.Equal(3, outcome.RebuiltVersion);
        Assert.Equal(3, await operationalStore.GetVersionAsync(sagaId));
    }

    [Fact]
    public async Task Concurrent_saves_at_same_expected_version_only_one_wins_and_only_one_journal_entry_exists()
    {
        await using var provider = BuildSplitStoreProvider($"journal-concurrency-{Guid.NewGuid():N}");
        var sagaId = Guid.NewGuid();

        await using var scopeA = provider.CreateAsyncScope();
        await using var scopeB = provider.CreateAsyncScope();
        var storeA = (IVersionedSagaStore)scopeA.ServiceProvider.GetRequiredService<ISagaStore>();
        var storeB = (IVersionedSagaStore)scopeB.ServiceProvider.GetRequiredService<ISagaStore>();

        var dataA = new DummySagaData { Counter = 1 };
        var dataB = new DummySagaData { Counter = 2 };

        var taskA = storeA.SaveSagaDataAsync(sagaId, dataA, 0);
        var taskB = storeB.SaveSagaDataAsync(sagaId, dataB, 0);

        var results = await Task.WhenAll(
            Wrap(taskA),
            Wrap(taskB));

        Assert.Single(results, r => r.Succeeded);
        Assert.Single(results, r => !r.Succeeded && r.Exception is SagaConcurrencyException);

        await using var verifyScope = provider.CreateAsyncScope();
        var journalStore = verifyScope.ServiceProvider.GetRequiredService<ISagaJournalStore>();
        var entries = await journalStore.ReadAsync(sagaId, 0, 10);
        Assert.Single(entries, x => x.TargetVersion == 1);

        static async Task<(bool Succeeded, Exception? Exception)> Wrap(Task<long> task)
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
    }

    [Fact]
    public async Task Redelivered_transition_does_not_create_a_duplicate_journal_row()
    {
        // Proves the equivalent of duplicate Inbox delivery at the store level: appending the exact same
        // transition twice (same TransitionId, as a redelivered message would produce) never creates a
        // second row. See task report for why this form was chosen over wiring a full SagaDispatcher.
        await using var provider = BuildSplitStoreProvider($"journal-redelivery-{Guid.NewGuid():N}");
        var sagaId = Guid.NewGuid();

        await using var scope = provider.CreateAsyncScope();
        var journalStore = scope.ServiceProvider.GetRequiredService<ISagaJournalStore>();

        var entry = new SagaJournalEntry
        {
            JournalEntryId = Guid.NewGuid(),
            TransitionId = Guid.NewGuid(),
            SagaId = sagaId,
            SequenceNumber = 1,
            PreviousVersion = 0,
            TargetVersion = 1,
            TransitionType = SagaJournalTransitionType.Created,
            SagaDataTypeName = typeof(DummySagaData).AssemblyQualifiedName!,
            SagaDataPayload = $"{{\"SagaId\":\"{sagaId}\",\"Version\":1}}",
            CreatedAtUtc = DateTime.UtcNow
        };

        // First delivery.
        await journalStore.AppendAsync(entry);
        // Redelivery of the exact same logical transition (same TransitionId).
        await journalStore.AppendAsync(entry);

        var entries = await journalStore.ReadAsync(sagaId, 0, 10);
        Assert.Single(entries);
    }

    private ServiceProvider BuildSplitStoreProvider(string applicationId)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ApplicationId"] = applicationId })
            .Build();

        var services = new ServiceCollection();
        var builder = services.AddLycia(configuration);
        builder.UseTransport().InMemory();
        builder.UsePersistence()
            .WithPostgreSqlCanonicalSagaStore(o => o.ConnectionString = pgFixture.ConnectionString)
            .WithRedisOperationalSagaStore(o => o.ConnectionString = redisFixture.ConnectionString)
            .UseSplitStore();
        builder.Build();

        return services.BuildServiceProvider();
    }
}
