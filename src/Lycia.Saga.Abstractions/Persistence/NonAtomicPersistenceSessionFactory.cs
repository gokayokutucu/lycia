// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
namespace Lycia.Saga.Abstractions.Persistence;

/// <summary>Default <see cref="ILyciaPersistenceSessionFactory"/> for non-relational SagaStore providers (InMemory, Redis).</summary>
public sealed class NonAtomicPersistenceSessionFactory : ILyciaPersistenceSessionFactory
{
    public Task<ILyciaPersistenceSession> BeginAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<ILyciaPersistenceSession>(NonAtomicPersistenceSession.Instance);
}
