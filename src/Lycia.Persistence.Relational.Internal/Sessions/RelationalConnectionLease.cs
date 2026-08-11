// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using System.Data.Common;
using Lycia.Saga.Abstractions.Persistence;

namespace Lycia.Persistence.Relational.Internal.Sessions;

/// <summary>Uses the current scoped relational session or owns a standalone connection when no session is active.</summary>
public sealed class RelationalConnectionLease<TConnection, TTransaction> : IAsyncDisposable
    where TConnection : DbConnection
    where TTransaction : DbTransaction
{
    private readonly bool _ownsConnection;

    private RelationalConnectionLease(TConnection connection, TTransaction? transaction, bool ownsConnection)
    {
        Connection = connection;
        Transaction = transaction;
        _ownsConnection = ownsConnection;
    }

    /// <summary>The connection to use for the store operation.</summary>
    public TConnection Connection { get; }

    /// <summary>The shared transaction, or <c>null</c> for a standalone operation.</summary>
    public TTransaction? Transaction { get; }

    /// <summary>Whether this lease opened the connection and must own standalone transaction decisions.</summary>
    public bool OwnsConnection => _ownsConnection;

    /// <summary>Creates a lease from the active scoped session or opens a standalone connection.</summary>
    public static async Task<RelationalConnectionLease<TConnection, TTransaction>> OpenAsync(
        ILyciaPersistenceSessionAccessor? accessor,
        Func<TConnection> connectionFactory,
        CancellationToken cancellationToken = default)
    {
        if (accessor?.Current is IRelationalPersistenceSession session)
        {
            if (session.Connection is not TConnection connection || session.Transaction is not TTransaction transaction)
            {
                throw new InvalidOperationException(
                    $"The active persistence session is incompatible with {typeof(TConnection).Name}.");
            }

            return new RelationalConnectionLease<TConnection, TTransaction>(connection, transaction, false);
        }

        if (accessor?.Current != null)
            throw new InvalidOperationException("A non-relational persistence session cannot enlist a relational store operation.");

        var owned = connectionFactory();
        try
        {
            await owned.OpenAsync(cancellationToken).ConfigureAwait(false);
            return new RelationalConnectionLease<TConnection, TTransaction>(owned, null, true);
        }
        catch
        {
            owned.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (!_ownsConnection) return;
#if NET8_0_OR_GREATER
        await Connection.DisposeAsync().ConfigureAwait(false);
#else
        Connection.Dispose();
        await Task.CompletedTask;
#endif
    }
}
