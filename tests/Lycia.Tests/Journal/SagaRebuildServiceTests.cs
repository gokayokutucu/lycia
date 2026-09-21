// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
using Lycia.Extensions.Journal;
using Lycia.Persistence.InMemory;
using Lycia.Stores;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Persistence.Journal;
using Lycia.Saga.Abstractions.Persistence.Reconciliation;

namespace Lycia.Tests.Journal;

public class SagaRebuildServiceTests
{
    private static SagaJournalEntry Entry(Guid sagaId, long previous, long target, SagaJournalTransitionType type = SagaJournalTransitionType.Updated, int schemaVersion = 1) =>
        new()
        {
            JournalEntryId = Guid.NewGuid(),
            TransitionId = Guid.NewGuid(),
            SagaId = sagaId,
            SequenceNumber = target,
            PreviousVersion = previous,
            TargetVersion = target,
            JournalSchemaVersion = schemaVersion,
            TransitionType = type,
            SagaDataTypeName = "TestSagaData",
            SagaDataPayload = $"{{\"version\":{target}}}",
            CreatedAtUtc = DateTime.UtcNow
        };

    private static SagaRebuildService CreateService(InMemorySagaJournalStore journalStore,
        FakeOperationalSagaProjectionStore? operationalStore = null, ISagaStore? canonicalStore = null,
        IEnumerable<IJournalEntryUpcaster>? upcasters = null) =>
        new(journalStore, new SagaJournalReducer(), operationalStore ?? new FakeOperationalSagaProjectionStore(),
            canonicalStore ?? new InMemorySagaStore(null!, null!, null!),
            new JournalEntryUpcastChain(upcasters ?? []));

    [Fact]
    public async Task RebuildSagaAsync_Installs_Journal_Derived_State_Into_Operational_Projection()
    {
        var journalStore = new InMemorySagaJournalStore();
        var operationalStore = new FakeOperationalSagaProjectionStore();
        var sagaId = Guid.NewGuid();
        await journalStore.AppendAsync(Entry(sagaId, 0, 1, SagaJournalTransitionType.Created));
        await journalStore.AppendAsync(Entry(sagaId, 1, 2));
        await journalStore.AppendAsync(Entry(sagaId, 2, 3, SagaJournalTransitionType.Completed));

        var service = CreateService(journalStore, operationalStore);
        var outcome = await service.RebuildSagaAsync(sagaId);

        Assert.True(outcome.Succeeded);
        Assert.Equal(3, outcome.RebuiltVersion);
        Assert.Equal(3, await operationalStore.GetVersionAsync(sagaId));
    }

    [Fact]
    public async Task RebuildSagaAsync_On_Missing_Projection_Installs_It()
    {
        var journalStore = new InMemorySagaJournalStore();
        var operationalStore = new FakeOperationalSagaProjectionStore();
        var sagaId = Guid.NewGuid();
        await journalStore.AppendAsync(Entry(sagaId, 0, 1, SagaJournalTransitionType.Created));

        Assert.Equal(0, await operationalStore.GetVersionAsync(sagaId));
        var outcome = await CreateService(journalStore, operationalStore).RebuildSagaAsync(sagaId);

        Assert.True(outcome.Succeeded);
        Assert.Equal(1, await operationalStore.GetVersionAsync(sagaId));
    }

    [Fact]
    public async Task RebuildSagaAsync_Does_Not_Overwrite_A_Newer_Live_Projection()
    {
        var journalStore = new InMemorySagaJournalStore();
        var operationalStore = new FakeOperationalSagaProjectionStore();
        var sagaId = Guid.NewGuid();
        await journalStore.AppendAsync(Entry(sagaId, 0, 1, SagaJournalTransitionType.Created));

        // Simulate a live transition that has already advanced beyond the rebuild target.
        await operationalStore.ApplyAsync(new SagaProjectionIntent
        {
            SagaId = sagaId, TargetVersion = 5, SagaDataType = "TestSagaData", Payload = "{}"
        });

        await CreateService(journalStore, operationalStore).RebuildSagaAsync(sagaId);

        Assert.Equal(5, await operationalStore.GetVersionAsync(sagaId));
    }

    [Fact]
    public async Task RebuildSagaAsync_Twice_Is_Idempotent_And_Does_Not_Advance_Version_Twice()
    {
        var journalStore = new InMemorySagaJournalStore();
        var operationalStore = new FakeOperationalSagaProjectionStore();
        var sagaId = Guid.NewGuid();
        await journalStore.AppendAsync(Entry(sagaId, 0, 1, SagaJournalTransitionType.Created));

        var service = CreateService(journalStore, operationalStore);
        await service.RebuildSagaAsync(sagaId);
        var second = await service.RebuildSagaAsync(sagaId);

        Assert.True(second.Succeeded);
        Assert.Equal(1, await operationalStore.GetVersionAsync(sagaId));
    }

    [Fact]
    public async Task RebuildAllAsync_Rebuilds_Every_Saga_And_Isolates_One_Corrupt_Saga()
    {
        var journalStore = new InMemorySagaJournalStore();
        var operationalStore = new FakeOperationalSagaProjectionStore();
        var healthySagaIds = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToList();
        foreach (var sagaId in healthySagaIds)
            await journalStore.AppendAsync(Entry(sagaId, 0, 1, SagaJournalTransitionType.Created));

        var corruptSagaId = Guid.NewGuid();
        journalStore.SeedRaw(Entry(corruptSagaId, 0, 1, SagaJournalTransitionType.Created));
        journalStore.SeedRaw(Entry(corruptSagaId, 5, 6)); // Gap: previous should have been 1.

        var summary = await CreateService(journalStore, operationalStore).RebuildAllAsync();

        Assert.Equal(4, summary.Processed);
        Assert.Equal(3, summary.Succeeded);
        Assert.Equal(1, summary.Failed);
        Assert.Contains(corruptSagaId, summary.FailedSagaIds);
        foreach (var sagaId in healthySagaIds)
            Assert.Equal(1, await operationalStore.GetVersionAsync(sagaId));
    }

    [Fact]
    public async Task RebuildAllAsync_Reports_Progress()
    {
        var journalStore = new InMemorySagaJournalStore();
        for (var i = 0; i < 3; i++)
            await journalStore.AppendAsync(Entry(Guid.NewGuid(), 0, 1, SagaJournalTransitionType.Created));

        var reports = new List<SagaRebuildProgress>();
        await CreateService(journalStore).RebuildAllAsync(progress: new Progress<SagaRebuildProgress>(reports.Add));

        // Progress.Report callbacks may be marshaled asynchronously; give them a moment to flush.
        await Task.Delay(50);
        Assert.NotEmpty(reports);
        Assert.Equal(3, reports[^1].Processed);
    }

    [Fact]
    public async Task RebuildAllAsync_Honors_Cancellation()
    {
        var journalStore = new InMemorySagaJournalStore();
        for (var i = 0; i < 5; i++)
            await journalStore.AppendAsync(Entry(Guid.NewGuid(), 0, 1, SagaJournalTransitionType.Created));

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var summary = await CreateService(journalStore).RebuildAllAsync(cancellationToken: cts.Token);

        Assert.True(summary.Cancelled);
        Assert.Equal(0, summary.Processed);
    }

    [Fact]
    public async Task VerifySagaAsync_Reports_Healthy_When_Versions_Agree()
    {
        var journalStore = new InMemorySagaJournalStore();
        var operationalStore = new FakeOperationalSagaProjectionStore();
        var sagaId = Guid.NewGuid();
        await journalStore.AppendAsync(Entry(sagaId, 0, 1, SagaJournalTransitionType.Created));
        await CreateService(journalStore, operationalStore).RebuildSagaAsync(sagaId);

        var result = await CreateService(journalStore, operationalStore).VerifySagaAsync(sagaId);

        Assert.Equal(SagaProjectionVerificationStatus.Healthy, result.Status);
        Assert.Equal(1, result.JournalVersion);
        Assert.Equal(1, result.OperationalProjectionVersion);
    }

    [Fact]
    public async Task VerifySagaAsync_Reports_MissingProjection_Without_Modifying_Anything()
    {
        var journalStore = new InMemorySagaJournalStore();
        var operationalStore = new FakeOperationalSagaProjectionStore();
        var sagaId = Guid.NewGuid();
        await journalStore.AppendAsync(Entry(sagaId, 0, 1, SagaJournalTransitionType.Created));

        var result = await CreateService(journalStore, operationalStore).VerifySagaAsync(sagaId);

        Assert.Equal(SagaProjectionVerificationStatus.MissingProjection, result.Status);
        Assert.Equal(0, await operationalStore.GetVersionAsync(sagaId)); // Verify must not install anything.
    }

    [Fact]
    public async Task VerifySagaAsync_Reports_VersionMismatch()
    {
        var journalStore = new InMemorySagaJournalStore();
        var operationalStore = new FakeOperationalSagaProjectionStore();
        var sagaId = Guid.NewGuid();
        await journalStore.AppendAsync(Entry(sagaId, 0, 1, SagaJournalTransitionType.Created));
        await journalStore.AppendAsync(Entry(sagaId, 1, 2));
        await operationalStore.ApplyAsync(new SagaProjectionIntent { SagaId = sagaId, TargetVersion = 1, SagaDataType = "x", Payload = "{}" });

        var result = await CreateService(journalStore, operationalStore).VerifySagaAsync(sagaId);

        Assert.Equal(SagaProjectionVerificationStatus.VersionMismatch, result.Status);
    }

    [Fact]
    public async Task VerifySagaAsync_On_Corrupt_Journal_Reports_JournalGap()
    {
        var journalStore = new InMemorySagaJournalStore();
        var sagaId = Guid.NewGuid();
        journalStore.SeedRaw(Entry(sagaId, 0, 1, SagaJournalTransitionType.Created));
        journalStore.SeedRaw(Entry(sagaId, 5, 6)); // Gap.

        var result = await CreateService(journalStore).VerifySagaAsync(sagaId);

        Assert.Equal(SagaProjectionVerificationStatus.JournalGap, result.Status);
    }

    [Fact]
    public async Task VerifySagaAsync_On_Unsupported_Schema_Without_Upcaster_Reports_SchemaUnsupported()
    {
        var journalStore = new InMemorySagaJournalStore();
        var sagaId = Guid.NewGuid();
        journalStore.SeedRaw(Entry(sagaId, 0, 1, SagaJournalTransitionType.Created, schemaVersion: 0));

        var result = await CreateService(journalStore).VerifySagaAsync(sagaId); // No upcasters registered.

        Assert.Equal(SagaProjectionVerificationStatus.SchemaUnsupported, result.Status);
    }

    [Fact]
    public async Task RebuildSagaAsync_Backward_Transition_Is_Rejected_As_CorruptEntry()
    {
        var journalStore = new InMemorySagaJournalStore();
        var sagaId = Guid.NewGuid();
        journalStore.SeedRaw(Entry(sagaId, 0, 1, SagaJournalTransitionType.Created));
        journalStore.SeedRaw(new SagaJournalEntry
        {
            JournalEntryId = Guid.NewGuid(),
            TransitionId = Guid.NewGuid(),
            SagaId = sagaId,
            SequenceNumber = 1,
            PreviousVersion = 2, // Backward: PreviousVersion > current folded version.
            TargetVersion = 1,
            JournalSchemaVersion = 1,
            SagaDataTypeName = "TestSagaData",
            SagaDataPayload = "{}",
            CreatedAtUtc = DateTime.UtcNow
        });

        var outcome = await CreateService(journalStore).RebuildSagaAsync(sagaId);

        Assert.False(outcome.Succeeded);
        Assert.Equal(SagaJournalFailureKind.JournalGap, outcome.FailureKind);
    }

    [Fact]
    public async Task VerifyAllAsync_Aggregates_Multiple_Sagas()
    {
        var journalStore = new InMemorySagaJournalStore();
        var operationalStore = new FakeOperationalSagaProjectionStore();
        var healthySagaId = Guid.NewGuid();
        var missingSagaId = Guid.NewGuid();
        await journalStore.AppendAsync(Entry(healthySagaId, 0, 1, SagaJournalTransitionType.Created));
        await journalStore.AppendAsync(Entry(missingSagaId, 0, 1, SagaJournalTransitionType.Created));
        await CreateService(journalStore, operationalStore).RebuildSagaAsync(healthySagaId);

        var summary = await CreateService(journalStore, operationalStore).VerifyAllAsync();

        Assert.Equal(2, summary.Processed);
        Assert.Equal(1, summary.Succeeded); // Healthy.
        Assert.Equal(1, summary.Failed); // MissingProjection counts as not-healthy in the bulk summary.
    }
}
