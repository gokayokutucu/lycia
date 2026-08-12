// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
using Lycia.Extensions.Journal;
using Lycia.Saga.Abstractions.Persistence.Journal;

namespace Lycia.Tests.Journal;

public class SagaJournalReducerTests
{
    private static SagaJournalEntry Entry(Guid sagaId, long previous, long target, SagaJournalTransitionType type = SagaJournalTransitionType.Updated) =>
        new()
        {
            JournalEntryId = Guid.NewGuid(),
            TransitionId = Guid.NewGuid(),
            SagaId = sagaId,
            SequenceNumber = target,
            PreviousVersion = previous,
            TargetVersion = target,
            TransitionType = type,
            SagaDataTypeName = "TestSagaData",
            SagaDataPayload = $"{{\"version\":{target}}}",
            StepsSnapshotPayload = $"[{{\"step\":{target}}}]",
            CreatedAtUtc = DateTime.UtcNow
        };

    [Fact]
    public void Reduce_First_Entry_With_Null_Previous_State_Produces_Initial_State()
    {
        var reducer = new SagaJournalReducer();
        var sagaId = Guid.NewGuid();
        var entry = Entry(sagaId, 0, 1, SagaJournalTransitionType.Created);

        var state = reducer.Reduce(null, entry);

        Assert.Equal(sagaId, state.SagaId);
        Assert.Equal(1, state.Version);
        Assert.False(state.IsCompleted);
        Assert.False(state.IsFailed);
    }

    [Fact]
    public void Reduce_Multi_Step_Sequence_Reconstructs_Final_State_Deterministically()
    {
        var reducer = new SagaJournalReducer();
        var sagaId = Guid.NewGuid();
        SagaJournalState? state = null;
        state = reducer.Reduce(state, Entry(sagaId, 0, 1, SagaJournalTransitionType.Created));
        state = reducer.Reduce(state, Entry(sagaId, 1, 2));
        state = reducer.Reduce(state, Entry(sagaId, 2, 3, SagaJournalTransitionType.Completed));

        Assert.Equal(3, state.Version);
        Assert.True(state.IsCompleted);
    }

    [Fact]
    public void Reduce_Same_Journal_Produces_Identical_State_Every_Time()
    {
        var reducer = new SagaJournalReducer();
        var sagaId = Guid.NewGuid();
        var entries = new[]
        {
            Entry(sagaId, 0, 1, SagaJournalTransitionType.Created),
            Entry(sagaId, 1, 2),
            Entry(sagaId, 2, 3, SagaJournalTransitionType.Completed)
        };

        SagaJournalState? Replay()
        {
            SagaJournalState? s = null;
            foreach (var e in entries) s = reducer.Reduce(s, e);
            return s;
        }

        var first = Replay();
        var second = Replay();

        Assert.Equal(first!.Version, second!.Version);
        Assert.Equal(first.SagaDataPayload, second.SagaDataPayload);
        Assert.Equal(first.StepsSnapshotPayload, second.StepsSnapshotPayload);
        Assert.Equal(first.IsCompleted, second.IsCompleted);
    }

    [Fact]
    public void Reduce_Failed_Transition_Marks_State_Failed()
    {
        var reducer = new SagaJournalReducer();
        var sagaId = Guid.NewGuid();
        var state = reducer.Reduce(null, Entry(sagaId, 0, 1, SagaJournalTransitionType.Failed));

        Assert.True(state.IsFailed);
        Assert.False(state.IsCompleted);
    }

    [Fact]
    public void Reduce_Preserves_Steps_Snapshot_For_Compensation_And_Cancellation_Reconstruction()
    {
        var reducer = new SagaJournalReducer();
        var sagaId = Guid.NewGuid();
        var entry = Entry(sagaId, 0, 1);
        entry.StepsSnapshotPayload = "[{\"status\":\"Compensated\"},{\"status\":\"Cancelled\"}]";

        var state = reducer.Reduce(null, entry);

        Assert.Contains("Compensated", state.StepsSnapshotPayload);
        Assert.Contains("Cancelled", state.StepsSnapshotPayload);
    }
}
