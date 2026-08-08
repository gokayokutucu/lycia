// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
namespace Lycia.Saga.Exceptions;

/// <summary>
/// Thrown when a versioned <c>ISagaStore</c> write's expected version does not match the currently
/// stored version, i.e. another writer already advanced the saga since the caller last loaded it.
/// </summary>
public class SagaConcurrencyException(Guid sagaId, long expectedVersion, long actualVersion)
    : LyciaSagaException(
        $"Concurrency conflict saving saga '{sagaId}': expected version {expectedVersion}, actual version {actualVersion}.")
{
    public Guid SagaId { get; } = sagaId;
    public long ExpectedVersion { get; } = expectedVersion;
    public long ActualVersion { get; } = actualVersion;
}
