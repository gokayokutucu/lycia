// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
namespace Lycia.Saga.Abstractions.Persistence;

/// <summary>
/// Provider-neutral unit-of-work boundary, opened via <see cref="ILyciaPersistenceSessionFactory"/>.
/// Lets SagaStore/Inbox/Outbox operations that support it participate in one atomic commit; stores
/// that cannot join a real transaction (Redis, InMemory) return a session with
/// <see cref="SupportsAtomicTransactions"/> set to <c>false</c> instead of pretending to be atomic.
/// </summary>
public interface ILyciaPersistenceSession : IAsyncDisposable
{
    /// <summary>
    /// <c>true</c> when <see cref="CommitAsync"/>/<see cref="RollbackAsync"/> provide a real atomic
    /// boundary (a relational transaction). <c>false</c> for providers where this session is a
    /// logical no-op grouping only.
    /// </summary>
    bool SupportsAtomicTransactions { get; }

    Task CommitAsync(CancellationToken cancellationToken = default);

    Task RollbackAsync(CancellationToken cancellationToken = default);
}
