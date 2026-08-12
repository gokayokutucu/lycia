// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
using Lycia.Persistence.Relational.Internal.Sessions;
using Lycia.Saga.Abstractions.Persistence;
using Lycia.Saga.Abstractions.Persistence.Journal;
using Microsoft.Data.SqlClient;

namespace Lycia.Persistence.SqlServer;

/// <summary>SQL Server durable canonical store for the immutable saga transition journal.</summary>
public sealed class SqlServerSagaJournalStore(
    SqlServerSagaStoreOptions options,
    ILyciaPersistenceSessionAccessor? sessionAccessor) : ISagaJournalStore
{
    private string Table => $"{options.SchemaName}.LyciaSagaJournal";
    private SqlConnection CreateConnection() => new(options.ConnectionString);

    /// <inheritdoc />
    public async Task AppendAsync(SagaJournalEntry entry, CancellationToken cancellationToken = default)
    {
        if (entry == null) throw new ArgumentNullException(nameof(entry));

        await using var lease = await RelationalConnectionLease<SqlConnection, SqlTransaction>.OpenAsync(
            sessionAccessor, CreateConnection, cancellationToken).ConfigureAwait(false);
        using var command = CreateCommand(lease, $"""
            IF NOT EXISTS (SELECT 1 FROM {Table} WHERE TransitionId=@transitionId)
            INSERT INTO {Table}
                (JournalEntryId,TransitionId,SagaId,SequenceNumber,PreviousVersion,TargetVersion,
                 MessageId,RequestId,CorrelationId,CausationId,ParentMessageId,ApplicationId,HandlerType,MessageType,
                 MessageSchemaVersion,JournalSchemaVersion,TransitionType,SagaDataTypeName,SagaDataPayload,
                 StepsSnapshotPayload,CreatedAtUtc)
            VALUES
                (@journalEntryId,@transitionId,@sagaId,@sequenceNumber,@previousVersion,@targetVersion,
                 @messageId,@requestId,@correlationId,@causationId,@parentMessageId,@applicationId,@handlerType,@messageType,
                 @messageSchemaVersion,@journalSchemaVersion,@transitionType,@sagaDataTypeName,@sagaDataPayload,
                 @stepsSnapshotPayload,@createdAt);
            """);
        AddParameters(command, entry);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SagaJournalEntry>> ReadAsync(Guid sagaId, long afterVersion, int maxCount,
        CancellationToken cancellationToken = default)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandTimeout = options.CommandTimeoutSeconds;
        command.CommandText = $"""
            SELECT TOP (@maxCount) JournalEntryId,TransitionId,SagaId,SequenceNumber,PreviousVersion,TargetVersion,
                MessageId,RequestId,CorrelationId,CausationId,ParentMessageId,ApplicationId,HandlerType,MessageType,
                MessageSchemaVersion,JournalSchemaVersion,TransitionType,SagaDataTypeName,SagaDataPayload,
                StepsSnapshotPayload,CreatedAtUtc
            FROM {Table}
            WHERE SagaId=@sagaId AND TargetVersion>@afterVersion
            ORDER BY SequenceNumber ASC;
            """;
        command.Parameters.AddWithValue("@sagaId", sagaId);
        command.Parameters.AddWithValue("@afterVersion", afterVersion);
        command.Parameters.AddWithValue("@maxCount", maxCount);

        var list = new List<SagaJournalEntry>();
        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) list.Add(Read(reader));
        return list;
    }

    /// <inheritdoc />
    public async Task<long> GetLatestVersionAsync(Guid sagaId, CancellationToken cancellationToken = default)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandTimeout = options.CommandTimeoutSeconds;
        command.CommandText = $"SELECT MAX(TargetVersion) FROM {Table} WHERE SagaId=@sagaId;";
        command.Parameters.AddWithValue("@sagaId", sagaId);
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result == null || result == DBNull.Value ? 0 : (long)result;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> EnumerateSagaIdsAsync(Guid? afterSagaId, int maxCount,
        CancellationToken cancellationToken = default)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandTimeout = options.CommandTimeoutSeconds;
        command.CommandText = $"""
            SELECT TOP (@maxCount) SagaId FROM
                (SELECT DISTINCT SagaId FROM {Table} WHERE @afterSagaId IS NULL OR SagaId > @afterSagaId) x
            ORDER BY SagaId;
            """;
        command.Parameters.AddWithValue("@afterSagaId", (object?)afterSagaId ?? DBNull.Value);
        command.Parameters.AddWithValue("@maxCount", maxCount);

        var list = new List<Guid>();
        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) list.Add(reader.GetGuid(0));
        return list;
    }

    private SqlCommand CreateCommand(RelationalConnectionLease<SqlConnection, SqlTransaction> lease, string sql)
    {
        var command = lease.Connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = options.CommandTimeoutSeconds;
        if (lease.Transaction != null) command.Transaction = lease.Transaction;
        return command;
    }

    private static void AddParameters(SqlCommand command, SagaJournalEntry entry)
    {
        command.Parameters.AddWithValue("@journalEntryId", entry.JournalEntryId);
        command.Parameters.AddWithValue("@transitionId", entry.TransitionId);
        command.Parameters.AddWithValue("@sagaId", entry.SagaId);
        command.Parameters.AddWithValue("@sequenceNumber", entry.SequenceNumber);
        command.Parameters.AddWithValue("@previousVersion", entry.PreviousVersion);
        command.Parameters.AddWithValue("@targetVersion", entry.TargetVersion);
        command.Parameters.AddWithValue("@messageId", (object?)entry.MessageId ?? DBNull.Value);
        command.Parameters.AddWithValue("@requestId", (object?)entry.RequestId ?? DBNull.Value);
        command.Parameters.AddWithValue("@correlationId", (object?)entry.CorrelationId ?? DBNull.Value);
        command.Parameters.AddWithValue("@causationId", (object?)entry.CausationId ?? DBNull.Value);
        command.Parameters.AddWithValue("@parentMessageId", (object?)entry.ParentMessageId ?? DBNull.Value);
        command.Parameters.AddWithValue("@applicationId", (object?)entry.ApplicationId ?? DBNull.Value);
        command.Parameters.AddWithValue("@handlerType", (object?)entry.HandlerType ?? DBNull.Value);
        command.Parameters.AddWithValue("@messageType", (object?)entry.MessageType ?? DBNull.Value);
        command.Parameters.AddWithValue("@messageSchemaVersion", entry.MessageSchemaVersion);
        command.Parameters.AddWithValue("@journalSchemaVersion", entry.JournalSchemaVersion);
        command.Parameters.AddWithValue("@transitionType", (int)entry.TransitionType);
        command.Parameters.AddWithValue("@sagaDataTypeName", entry.SagaDataTypeName);
        command.Parameters.AddWithValue("@sagaDataPayload", entry.SagaDataPayload);
        command.Parameters.AddWithValue("@stepsSnapshotPayload", (object?)entry.StepsSnapshotPayload ?? DBNull.Value);
        command.Parameters.AddWithValue("@createdAt", entry.CreatedAtUtc);
    }

    private static SagaJournalEntry Read(SqlDataReader reader) => new()
    {
        JournalEntryId = reader.GetGuid(0),
        TransitionId = reader.GetGuid(1),
        SagaId = reader.GetGuid(2),
        SequenceNumber = reader.GetInt64(3),
        PreviousVersion = reader.GetInt64(4),
        TargetVersion = reader.GetInt64(5),
        MessageId = reader.IsDBNull(6) ? null : reader.GetGuid(6),
        RequestId = reader.IsDBNull(7) ? null : reader.GetGuid(7),
        CorrelationId = reader.IsDBNull(8) ? null : reader.GetGuid(8),
        CausationId = reader.IsDBNull(9) ? null : reader.GetGuid(9),
        ParentMessageId = reader.IsDBNull(10) ? null : reader.GetGuid(10),
        ApplicationId = reader.IsDBNull(11) ? null : reader.GetString(11),
        HandlerType = reader.IsDBNull(12) ? null : reader.GetString(12),
        MessageType = reader.IsDBNull(13) ? null : reader.GetString(13),
        MessageSchemaVersion = reader.GetInt32(14),
        JournalSchemaVersion = reader.GetInt32(15),
        TransitionType = (SagaJournalTransitionType)reader.GetInt32(16),
        SagaDataTypeName = reader.GetString(17),
        SagaDataPayload = reader.GetString(18),
        StepsSnapshotPayload = reader.IsDBNull(19) ? null : reader.GetString(19),
        CreatedAtUtc = reader.GetDateTime(20)
    };
}
