// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
using Lycia.Extensions.Journal;
using Lycia.Saga.Abstractions.Persistence.Journal;

namespace Lycia.Tests.Journal;

public class JournalEntryUpcastChainTests
{
    /// <summary>
    /// A deterministic fixture upcaster simulating a real schema evolution: an old (fictional) V0 entry
    /// stored SagaDataPayload without a "schemaNote" marker; V1 (current) adds one. This proves the
    /// upcast chain converges to a reducer-compatible entry without mutating the persisted original.
    /// </summary>
    private sealed class V0ToV1Upcaster : IJournalEntryUpcaster
    {
        public int FromSchemaVersion => 0;
        public int ToSchemaVersion => 1;

        public SagaJournalEntry Upcast(SagaJournalEntry entry) => new()
        {
            JournalEntryId = entry.JournalEntryId,
            TransitionId = entry.TransitionId,
            SagaId = entry.SagaId,
            SequenceNumber = entry.SequenceNumber,
            PreviousVersion = entry.PreviousVersion,
            TargetVersion = entry.TargetVersion,
            MessageId = entry.MessageId,
            RequestId = entry.RequestId,
            CorrelationId = entry.CorrelationId,
            CausationId = entry.CausationId,
            ParentMessageId = entry.ParentMessageId,
            ApplicationId = entry.ApplicationId,
            HandlerType = entry.HandlerType,
            MessageType = entry.MessageType,
            MessageSchemaVersion = entry.MessageSchemaVersion,
            JournalSchemaVersion = ToSchemaVersion,
            TransitionType = entry.TransitionType,
            SagaDataTypeName = entry.SagaDataTypeName,
            SagaDataPayload = entry.SagaDataPayload.Replace("\"legacy\":true", "\"legacy\":true,\"schemaNote\":\"upcast-from-v0\""),
            StepsSnapshotPayload = entry.StepsSnapshotPayload,
            CreatedAtUtc = entry.CreatedAtUtc
        };
    }

    private static SagaJournalEntry LegacyEntry(Guid sagaId) => new()
    {
        JournalEntryId = Guid.NewGuid(),
        TransitionId = Guid.NewGuid(),
        SagaId = sagaId,
        SequenceNumber = 1,
        PreviousVersion = 0,
        TargetVersion = 1,
        JournalSchemaVersion = 0, // Older than SagaJournalSchema.CurrentVersion (1).
        TransitionType = SagaJournalTransitionType.Created,
        SagaDataTypeName = "TestSagaData",
        SagaDataPayload = "{\"legacy\":true}",
        CreatedAtUtc = DateTime.UtcNow
    };

    [Fact]
    public void Upcast_Converges_To_Current_Schema_Version_And_Reduces_To_Expected_State()
    {
        var chain = new JournalEntryUpcastChain([new V0ToV1Upcaster()]);
        var original = LegacyEntry(Guid.NewGuid());

        var result = chain.Upcast(original);

        Assert.True(result.Succeeded);
        Assert.Equal(SagaJournalSchema.CurrentVersion, result.Entry!.JournalSchemaVersion);
        Assert.Contains("schemaNote", result.Entry.SagaDataPayload);
        // The original persisted record must not be mutated by upcasting.
        Assert.Equal(0, original.JournalSchemaVersion);
        Assert.DoesNotContain("schemaNote", original.SagaDataPayload);

        var reducer = new SagaJournalReducer();
        var state = reducer.Reduce(null, result.Entry);
        Assert.Equal(1, state.Version);
    }

    [Fact]
    public void Upcast_Without_Registered_Upcaster_Fails_Clearly()
    {
        var chain = new JournalEntryUpcastChain([]); // No upcasters registered.
        var original = LegacyEntry(Guid.NewGuid());

        var result = chain.Upcast(original);

        Assert.False(result.Succeeded);
        Assert.Contains("No upcaster", result.FailureReason);
    }
}
