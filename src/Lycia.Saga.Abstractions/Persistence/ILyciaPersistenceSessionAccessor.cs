// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using System.Data.Common;

namespace Lycia.Saga.Abstractions.Persistence;

/// <summary>
/// Holds the explicitly owned persistence session for the current dependency-injection scope.
/// This is scoped state and does not use ambient static or <c>AsyncLocal</c> storage.
/// </summary>
public interface ILyciaPersistenceSessionAccessor
{
    /// <summary>Gets or sets the session owned by the current handler-processing scope.</summary>
    ILyciaPersistenceSession? Current { get; set; }
}

/// <summary>Default scoped implementation of <see cref="ILyciaPersistenceSessionAccessor"/>.</summary>
public sealed class LyciaPersistenceSessionAccessor : ILyciaPersistenceSessionAccessor
{
    /// <inheritdoc />
    public ILyciaPersistenceSession? Current { get; set; }
}

/// <summary>Exposes the provider-neutral relational connection and transaction owned by a session.</summary>
public interface IRelationalPersistenceSession : ILyciaPersistenceSession
{
    /// <summary>The open connection shared by enlisted relational stores.</summary>
    DbConnection Connection { get; }

    /// <summary>The active transaction shared by enlisted relational stores.</summary>
    DbTransaction Transaction { get; }
}

/// <summary>
/// Indicates that commit was issued but the application could not determine whether the database committed.
/// Callers must recover using durable message, Inbox, Saga, and Outbox identities instead of rerunning blindly.
/// </summary>
public sealed class PersistenceCommitOutcomeUnknownException : Exception
{
    /// <summary>Creates an indeterminate-commit exception.</summary>
    public PersistenceCommitOutcomeUnknownException(Exception innerException)
        : base("The persistence commit outcome is unknown. Durable identities must be checked before retrying handler logic.", innerException)
    {
    }
}
