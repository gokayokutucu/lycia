// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
using Lycia.Persistence.Relational.Internal.Sessions;
using Lycia.Saga.Abstractions.Persistence;
using Lycia.Saga.Abstractions.Persistence.Reconciliation;
using Microsoft.Data.SqlClient;

namespace Lycia.Persistence.SqlServer;

/// <summary>SQL Server durable canonical store for Split Store projection intents.</summary>
public sealed class SqlServerReconciliationStore(
    SqlServerSagaStoreOptions options,
    ILyciaPersistenceSessionAccessor? sessionAccessor) : IReconciliationStore
{
    private string Table => $"{options.SchemaName}.LyciaSagaReconciliation";
    private SqlConnection CreateConnection() => new(options.ConnectionString);

    /// <inheritdoc />
    public async Task AddAsync(SagaProjectionIntent intent, CancellationToken cancellationToken = default)
    {
        await using var lease = await RelationalConnectionLease<SqlConnection, SqlTransaction>.OpenAsync(
            sessionAccessor, CreateConnection).ConfigureAwait(false);
        using var command = CreateCommand(lease, $"""
            IF NOT EXISTS (SELECT 1 FROM {Table} WHERE TransitionId=@transitionId)
            INSERT INTO {Table} (TransitionId,SagaId,MessageId,ExpectedVersion,TargetVersion,SagaDataType,Payload,Status,CreatedAtUtc,UpdatedAtUtc)
            VALUES (@transitionId,@sagaId,@messageId,@expectedVersion,@targetVersion,@dataType,@payload,@status,@createdAt,@createdAt);
            """);
        AddParameters(command, intent);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SagaProjectionIntent>> ClaimAsync(string workerId, int batchSize, int maxAttempts,
        TimeSpan claimTimeout, CancellationToken cancellationToken = default)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            ;WITH candidates AS (
              SELECT TOP (@batchSize) * FROM {Table} WITH (UPDLOCK, READPAST, ROWLOCK)
              WHERE AttemptCount < @maxAttempts AND
               ((Status IN (@pending,@retry) AND (NextAttemptAtUtc IS NULL OR NextAttemptAtUtc <= SYSUTCDATETIME()))
                OR (Status=@claimed AND ClaimedAtUtc < DATEADD(millisecond,-@claimTimeoutMs,SYSUTCDATETIME())))
              ORDER BY SagaId, TargetVersion
            )
            UPDATE candidates SET Status=@claimed,WorkerId=@workerId,ClaimedAtUtc=SYSUTCDATETIME(),
              LastAttemptAtUtc=SYSUTCDATETIME(),AttemptCount=AttemptCount+1,UpdatedAtUtc=SYSUTCDATETIME()
            OUTPUT inserted.TransitionId,inserted.SagaId,inserted.MessageId,inserted.ExpectedVersion,inserted.TargetVersion,
              inserted.SagaDataType,inserted.Payload,inserted.Status,inserted.AttemptCount,inserted.CreatedAtUtc,inserted.NextAttemptAtUtc;
            """;
        command.Parameters.AddWithValue("@batchSize", batchSize);
        command.Parameters.AddWithValue("@maxAttempts", maxAttempts);
        command.Parameters.AddWithValue("@pending", (int)ReconciliationStatus.Pending);
        command.Parameters.AddWithValue("@retry", (int)ReconciliationStatus.RetryPending);
        command.Parameters.AddWithValue("@claimed", (int)ReconciliationStatus.Claimed);
        command.Parameters.AddWithValue("@claimTimeoutMs", (long)claimTimeout.TotalMilliseconds);
        command.Parameters.AddWithValue("@workerId", workerId);
        var list = new List<SagaProjectionIntent>();
        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) list.Add(Read(reader));
        return list;
    }

    /// <inheritdoc />
    public Task MarkCompletedAsync(Guid transitionId, ReconciliationStatus status,
        CancellationToken cancellationToken = default) => UpdateAsync(transitionId,status,null,null,cancellationToken);
    /// <inheritdoc />
    public Task MarkRetryAsync(Guid transitionId, DateTime nextAttemptAtUtc, string failureCode,
        CancellationToken cancellationToken = default) => UpdateAsync(transitionId,ReconciliationStatus.RetryPending,nextAttemptAtUtc,failureCode,cancellationToken);
    /// <inheritdoc />
    public Task MarkFailedAsync(Guid transitionId, string failureCode, CancellationToken cancellationToken = default) =>
        UpdateAsync(transitionId,ReconciliationStatus.Failed,null,failureCode,cancellationToken);

    /// <inheritdoc />
    public async Task<bool> QueueLatestAsync(Guid sagaId, CancellationToken cancellationToken = default)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = $"""
          UPDATE {Table} SET Status=@pending,AttemptCount=0,NextAttemptAtUtc=NULL,WorkerId=NULL,ClaimedAtUtc=NULL,FailureCode=NULL,UpdatedAtUtc=SYSUTCDATETIME()
          WHERE TransitionId=(SELECT TOP 1 TransitionId FROM {Table} WHERE SagaId=@sagaId ORDER BY TargetVersion DESC);
          """;
        command.Parameters.AddWithValue("@pending",(int)ReconciliationStatus.Pending);
        command.Parameters.AddWithValue("@sagaId",sagaId);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false)>0;
    }

    private async Task UpdateAsync(Guid id,ReconciliationStatus status,DateTime? next,string? failure,CancellationToken token)
    {
        using var connection=CreateConnection(); await connection.OpenAsync(token).ConfigureAwait(false);
        using var command=connection.CreateCommand();
        command.CommandText=$"UPDATE {Table} SET Status=@status,NextAttemptAtUtc=@next,FailureCode=@failure,WorkerId=NULL,ClaimedAtUtc=NULL,UpdatedAtUtc=SYSUTCDATETIME() WHERE TransitionId=@id;";
        command.Parameters.AddWithValue("@status",(int)status); command.Parameters.AddWithValue("@next",(object?)next??DBNull.Value);
        command.Parameters.AddWithValue("@failure",(object?)failure??DBNull.Value); command.Parameters.AddWithValue("@id",id);
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private SqlCommand CreateCommand(RelationalConnectionLease<SqlConnection,SqlTransaction> lease,string sql)
    { var command=lease.Connection.CreateCommand(); command.CommandText=sql; command.CommandTimeout=options.CommandTimeoutSeconds; if(lease.Transaction!=null) command.Transaction=lease.Transaction; return command; }
    private static void AddParameters(SqlCommand command,SagaProjectionIntent intent)
    {
        command.Parameters.AddWithValue("@transitionId",intent.TransitionId); command.Parameters.AddWithValue("@sagaId",intent.SagaId);
        command.Parameters.AddWithValue("@messageId",(object?)intent.MessageId??DBNull.Value); command.Parameters.AddWithValue("@expectedVersion",intent.ExpectedVersion);
        command.Parameters.AddWithValue("@targetVersion",intent.TargetVersion); command.Parameters.AddWithValue("@dataType",intent.SagaDataType);
        command.Parameters.AddWithValue("@payload",intent.Payload); command.Parameters.AddWithValue("@status",(int)intent.Status); command.Parameters.AddWithValue("@createdAt",intent.CreatedAtUtc);
    }
    private static SagaProjectionIntent Read(SqlDataReader reader)=>new(){TransitionId=reader.GetGuid(0),SagaId=reader.GetGuid(1),MessageId=reader.IsDBNull(2)?null:reader.GetGuid(2),ExpectedVersion=reader.GetInt64(3),TargetVersion=reader.GetInt64(4),SagaDataType=reader.GetString(5),Payload=reader.GetString(6),Status=(ReconciliationStatus)reader.GetInt32(7),AttemptCount=reader.GetInt32(8),CreatedAtUtc=reader.GetDateTime(9),NextAttemptAtUtc=reader.IsDBNull(10)?null:reader.GetDateTime(10)};
}
