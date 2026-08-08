// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
namespace Lycia.Saga.Abstractions.Persistence;

/// <summary>Opens a new <see cref="ILyciaPersistenceSession"/>. Registered by the selected SagaStore provider.</summary>
public interface ILyciaPersistenceSessionFactory
{
    Task<ILyciaPersistenceSession> BeginAsync(CancellationToken cancellationToken = default);
}
