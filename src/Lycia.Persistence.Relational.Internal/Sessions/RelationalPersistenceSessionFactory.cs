// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using System.Data.Common;
using Lycia.Saga.Abstractions.Persistence;

namespace Lycia.Persistence.Relational.Internal.Sessions;

/// <summary>
/// Opens a <see cref="RelationalPersistenceSession"/> using a provider-supplied connection factory
/// (e.g. <c>() => new SqlConnection(connectionString)</c> or <c>() => new NpgsqlConnection(connectionString)</c>).
/// Kept driver-agnostic, like <c>RelationalMigrationRunner</c>: no SQL Server/PostgreSQL package
/// reference lives in this project.
/// </summary>
public sealed class RelationalPersistenceSessionFactory : ILyciaPersistenceSessionFactory
{
    private readonly Func<DbConnection> _connectionFactory;

    public RelationalPersistenceSessionFactory(Func<DbConnection> connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<ILyciaPersistenceSession> BeginAsync(CancellationToken cancellationToken = default)
    {
        var connection = _connectionFactory();
        await connection.OpenAsync(cancellationToken);

#if NET8_0_OR_GREATER
        var transaction = await connection.BeginTransactionAsync(cancellationToken);
#else
        var transaction = connection.BeginTransaction();
        await Task.CompletedTask;
#endif

        return new RelationalPersistenceSession(connection, transaction);
    }
}
