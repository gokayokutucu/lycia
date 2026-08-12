// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
namespace Lycia.Saga.Abstractions.Persistence.Journal;

/// <summary>
/// Pure, side-effect-free fold from (previous state, next journal entry) to new state. Implementations
/// must not resolve <c>IEventBus</c>, HTTP clients, or any application service; must not execute saga
/// or compensation handlers; must not publish, write Inbox/Outbox, schedule messages, read
/// <see cref="DateTime.UtcNow"/> as business input, or generate new identities. The same ordered
/// journal must always produce the same resulting state.
/// </summary>
public interface ISagaJournalReducer
{
    /// <summary>
    /// Folds one journal entry onto the previous state. <paramref name="previous"/> is <c>null</c> for
    /// the first entry of a saga. <paramref name="entry"/> must already be at the reducer's supported
    /// <see cref="SagaJournalEntry.JournalSchemaVersion"/> — upcasting happens before this call.
    /// </summary>
    SagaJournalState Reduce(SagaJournalState? previous, SagaJournalEntry entry);
}
