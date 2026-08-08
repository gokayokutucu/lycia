// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
namespace Lycia.Saga.Abstractions.Persistence;

/// <summary>
/// Default <see cref="ILyciaPersistenceSession"/> for providers that cannot join a real cross-store
/// transaction (InMemory, Redis). Commit/rollback are logical no-ops — there is nothing to roll back
/// structurally, and callers must check <see cref="SupportsAtomicTransactions"/> before relying on
/// atomicity rather than assuming this type behaves like a real transaction.
/// </summary>
public sealed class NonAtomicPersistenceSession : ILyciaPersistenceSession
{
    public static readonly NonAtomicPersistenceSession Instance = new();

    private NonAtomicPersistenceSession()
    {
    }

    public bool SupportsAtomicTransactions => false;

    public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task RollbackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public ValueTask DisposeAsync() => default;
}
