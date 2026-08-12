// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Lycia.Persistence.Relational.Internal.Migrations;
using Lycia.Persistence.Relational.Internal.Sessions;
using Lycia.Persistence.TestKit;
using Lycia.Saga.Abstractions.Persistence;
using Microsoft.Data.SqlClient;

namespace Lycia.Persistence.SqlServer.Tests;

/// <summary>
/// Phase 7 reliability-hardening coverage: failure windows not already exercised by the Phase 4/6
/// atomic and journal test suites (unknown-commit-outcome wrapping, and reconnect-after-restart).
/// </summary>
[Collection("SqlServerContainer")]
public class SqlServerFailureWindowTests(SqlServerContainerFixture fixture)
{
    /// <summary>
    /// Proves <see cref="RelationalPersistenceSession.CommitAsync"/> wraps ANY exception surfaced while
    /// issuing the commit into <see cref="PersistenceCommitOutcomeUnknownException"/>, deterministically.
    /// A real SQL Server socket-level "commit sent, ack lost" race is not reliably reproducible from a
    /// test process, so this uses a hand-rolled fake <see cref="DbConnection"/>/<see cref="DbTransaction"/>
    /// whose commit throws on demand — the same code path
    /// (<c>RelationalPersistenceSessionFactory.BeginAsync</c> + <c>RelationalPersistenceSession.CommitAsync</c>)
    /// that the real SQL Server/PostgreSQL sessions run, exercised through the public factory API only
    /// (no reliance on the session's internal constructor).
    /// </summary>
    [Fact]
    public async Task CommitAsync_wraps_arbitrary_post_commit_failure_in_PersistenceCommitOutcomeUnknownException()
    {
        var factory = new RelationalPersistenceSessionFactory(() => new ThrowingCommitConnection());
        var session = await factory.BeginAsync();

        var thrown = await Assert.ThrowsAsync<PersistenceCommitOutcomeUnknownException>(
            () => session.CommitAsync());

        Assert.IsType<InvalidOperationException>(thrown.InnerException);
        Assert.Equal("simulated commit acknowledgement failure", thrown.InnerException!.Message);

        await session.DisposeAsync();
    }

    /// <summary>
    /// A second, independent exception thrown during commit must never itself be re-wrapped: the
    /// exception type check in <c>CommitAsync</c> excludes <see cref="PersistenceCommitOutcomeUnknownException"/>
    /// specifically so a caller can't end up with nested wrapping.
    /// </summary>
    [Fact]
    public async Task CommitAsync_does_not_rewrap_an_already_wrapped_outcome_exception()
    {
        var original = new PersistenceCommitOutcomeUnknownException(new InvalidOperationException("inner"));
        var factory = new RelationalPersistenceSessionFactory(() => new ThrowingCommitConnection(original));
        var session = await factory.BeginAsync();

        var thrown = await Assert.ThrowsAsync<PersistenceCommitOutcomeUnknownException>(
            () => session.CommitAsync());

        // The catch guard `when (ex is not PersistenceCommitOutcomeUnknownException)` lets the original
        // instance propagate unwrapped rather than nesting a new exception around it.
        Assert.Same(original, thrown);
        Assert.IsType<InvalidOperationException>(thrown.InnerException);
        Assert.Equal("inner", thrown.InnerException!.Message);

        await session.DisposeAsync();
    }

    /// <summary>
    /// Proves a store can reconnect and keep operating after its underlying physical connections are
    /// torn down out from under it. A real <c>docker restart</c> of the container was tried first, but
    /// this container is shared (via <see cref="SqlServerContainerFixture"/>, an <c>ICollectionFixture</c>)
    /// by every other test in the same collection, and restarting it mid-run knocked every concurrently
    /// running test's connection out too (observed: the whole suite failed with "could not open a
    /// connection to SQL Server" once this test restarted the shared container). Clearing the ADO.NET
    /// connection pool for this connection string reproduces the same "reconnect after every physical
    /// connection is gone" condition without disturbing the shared container that other tests depend on.
    /// </summary>
    [Fact]
    public async Task SagaStore_reconnects_and_round_trips_after_connection_pool_reset()
    {
        var options = new SqlServerSagaStoreOptions
        {
            ConnectionString = fixture.ConnectionString,
            SchemaManagement = SchemaManagementMode.ApplyMigrations
        };
        await SqlServerSchemaMigrator.RunAsync(options);
        var storeBeforeReset = new SqlServerSagaStore(options, null!, null!, null!, null);
        var sagaId = Guid.NewGuid();
        await storeBeforeReset.SaveSagaDataAsync(sagaId, new DummySagaData { Payload = "before-reset" });

        // Force every pooled physical connection for this connection string closed, so the next store
        // operation must open a genuinely new physical connection to the same container.
        SqlConnection.ClearAllPools();

        var storeAfterReset = new SqlServerSagaStore(options, null!, null!, null!, null);
        var loaded = await storeAfterReset.LoadSagaDataAsync<DummySagaData>(sagaId);
        Assert.Equal("before-reset", loaded.Payload);

        // A fresh write after reconnect must also work normally.
        await storeAfterReset.SaveSagaDataAsync(sagaId, new DummySagaData { Payload = "after-reset" });
        var reloaded = await storeAfterReset.LoadSagaDataAsync<DummySagaData>(sagaId);
        Assert.Equal("after-reset", reloaded.Payload);
    }

    private sealed class ThrowingCommitConnection : DbConnection
    {
        private readonly Exception _exceptionToThrow;
        private ConnectionState _state = ConnectionState.Closed;

        public ThrowingCommitConnection(Exception? exceptionToThrow = null)
        {
            _exceptionToThrow = exceptionToThrow ?? new InvalidOperationException("simulated commit acknowledgement failure");
        }

        [AllowNull]
        public override string ConnectionString { get; set; } = string.Empty;
        public override string Database => "fake";
        public override string DataSource => "fake";
        public override string ServerVersion => "0.0";
        public override ConnectionState State => _state;

        public override void ChangeDatabase(string databaseName) { }
        public override void Close() => _state = ConnectionState.Closed;
        public override void Open() => _state = ConnectionState.Open;
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
            new ThrowingCommitTransaction(this, _exceptionToThrow);
        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
    }

    private sealed class ThrowingCommitTransaction(DbConnection connection, Exception exceptionToThrow) : DbTransaction
    {
        protected override DbConnection DbConnection { get; } = connection;
        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;

        public override void Commit() => throw exceptionToThrow;
        public override void Rollback() { }
    }
}
