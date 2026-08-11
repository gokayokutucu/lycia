// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using System.Data.Common;
using Lycia.Saga.Abstractions.Persistence;

namespace Lycia.Persistence.Relational.Internal.Sessions;

/// <summary>
/// A real relational transaction boundary shared by an open <see cref="DbConnection"/> and
/// <see cref="DbTransaction"/>. SQL Server/PostgreSQL SagaStore, Inbox, and Outbox operations that
/// accept a <see cref="DbTransaction"/> can enlist in the same session to prepare for atomic
/// Saga+Inbox+Outbox commits. Wiring those operations to actually share one session is future work —
/// this type only provides the boundary itself.
/// </summary>
public sealed class RelationalPersistenceSession : IRelationalPersistenceSession
{
    private readonly DbConnection _connection;
    private readonly DbTransaction _transaction;
    private bool _completed;
    private bool _commitIssued;

    internal RelationalPersistenceSession(DbConnection connection, DbTransaction transaction)
    {
        _connection = connection;
        _transaction = transaction;
    }

    /// <summary>The open connection backing this session's transaction. Relational stores enlist operations by passing this and <see cref="Transaction"/> to their commands.</summary>
    public DbConnection Connection => _connection;

    /// <summary>The active transaction. Relational stores must pass this to every command that should be part of this session.</summary>
    public DbTransaction Transaction => _transaction;

    /// <inheritdoc />
    public bool SupportsAtomicTransactions => true;

    /// <inheritdoc />
    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (_completed) throw new InvalidOperationException("The persistence session has already completed.");
        _commitIssued = true;
        try
        {
#if NET8_0_OR_GREATER
            await _transaction.CommitAsync(cancellationToken);
#else
            _transaction.Commit();
            await Task.CompletedTask;
#endif
            _completed = true;
        }
        catch (Exception ex) when (ex is not PersistenceCommitOutcomeUnknownException)
        {
            throw new PersistenceCommitOutcomeUnknownException(ex);
        }
    }

    /// <inheritdoc />
    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        if (_completed) throw new InvalidOperationException("The persistence session has already completed.");
        if (_commitIssued)
            throw new InvalidOperationException("Rollback cannot be asserted after commit was issued because the outcome may be unknown.");
#if NET8_0_OR_GREATER
        await _transaction.RollbackAsync(cancellationToken);
#else
        _transaction.Rollback();
        await Task.CompletedTask;
#endif
        _completed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_completed && !_commitIssued)
        {
            try
            {
#if NET8_0_OR_GREATER
                await _transaction.RollbackAsync();
#else
                _transaction.Rollback();
#endif
            }
            catch
            {
                // Connection may already be broken/closed; rollback-on-dispose is best-effort.
            }
        }

#if NET8_0_OR_GREATER
        await _transaction.DisposeAsync();
        await _connection.DisposeAsync();
#else
        _transaction.Dispose();
        _connection.Dispose();
        await Task.CompletedTask;
#endif
    }
}
