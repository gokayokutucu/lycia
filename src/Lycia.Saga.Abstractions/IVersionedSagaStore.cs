// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Saga.Abstractions.Messaging;

namespace Lycia.Saga.Abstractions;

/// <summary>
/// Optional capability implemented by <see cref="ISagaStore"/> providers that support explicit numeric
/// optimistic concurrency on saga data. A provider that does not implement this interface only supports
/// the last-write-wins <see cref="ISagaStore.SaveSagaDataAsync{TSagaData}"/> behavior.
/// </summary>
public interface IVersionedSagaStore
{
    /// <summary>
    /// Saves saga data only if the currently stored version equals <paramref name="expectedVersion"/>.
    /// Use <c>expectedVersion = 0</c> to mean "this saga must not already have data" (first insert).
    /// </summary>
    /// <returns>The new version after a successful save.</returns>
    /// <exception cref="Lycia.Saga.Exceptions.SagaConcurrencyException">
    /// Thrown when the stored version does not match <paramref name="expectedVersion"/>.
    /// </exception>
    Task<long> SaveSagaDataAsync<TSagaData>(Guid sagaId, TSagaData data, long expectedVersion)
        where TSagaData : SagaData;

    /// <summary>
    /// Loads the saga data together with its current version. Returns version 0 when the saga has no data yet.
    /// </summary>
    Task<(TSagaData Data, long Version)> LoadSagaDataWithVersionAsync<TSagaData>(Guid sagaId)
        where TSagaData : SagaData, new();
}
