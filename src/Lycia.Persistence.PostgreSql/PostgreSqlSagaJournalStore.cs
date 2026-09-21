// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
using Lycia.Persistence.Relational.Internal.Sessions;
using Lycia.Saga.Abstractions.Persistence;
using Lycia.Saga.Abstractions.Persistence.Journal;
using Npgsql;
using NpgsqlTypes;

namespace Lycia.Persistence.PostgreSql;

/// <summary>PostgreSQL durable canonical store for the immutable saga transition journal.</summary>
public sealed class PostgreSqlSagaJournalStore(
    PostgreSqlSagaStoreOptions options,
    ILyciaPersistenceSessionAccessor? sessionAccessor) : ISagaJournalStore
{
    private string Table => $"\"{options.SchemaName}\".lycia_saga_journal";
    private NpgsqlConnection CreateConnection() => new(options.BuildEffectiveConnectionString());

    /// <inheritdoc />
    public async Task AppendAsync(SagaJournalEntry entry, CancellationToken cancellationToken = default)
    {
        if (entry == null) throw new ArgumentNullException(nameof(entry));

        await using var lease = await RelationalConnectionLease<NpgsqlConnection, NpgsqlTransaction>.OpenAsync(
            sessionAccessor, CreateConnection).ConfigureAwait(false);
        using var command = CreateCommand(lease, $"""
            INSERT INTO {Table} (journal_entry_id, transition_id, saga_id, sequence_number, previous_version,
                target_version, message_id, request_id, correlation_id, causation_id, parent_message_id,
                application_id, handler_type, message_type, message_schema_version, journal_schema_version,
                transition_type, saga_data_type_name, saga_data_payload, steps_snapshot_payload, created_at_utc)
            VALUES (@journalEntryId, @transitionId, @sagaId, @sequenceNumber, @previousVersion,
                @targetVersion, @messageId, @requestId, @correlationId, @causationId, @parentMessageId,
                @applicationId, @handlerType, @messageType, @messageSchemaVersion, @journalSchemaVersion,
                @transitionType, @sagaDataTypeName, @sagaDataPayload, @stepsSnapshotPayload, @createdAt)
            ON CONFLICT (transition_id) DO NOTHING;
            """);
        AddEntryParameters(command, entry);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SagaJournalEntry>> ReadAsync(Guid sagaId, long afterVersion, int maxCount,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandTimeout = options.CommandTimeoutSeconds;
        command.CommandText = $"""
            SELECT journal_entry_id, transition_id, saga_id, sequence_number, previous_version, target_version,
                message_id, request_id, correlation_id, causation_id, parent_message_id, application_id,
                handler_type, message_type, message_schema_version, journal_schema_version, transition_type,
                saga_data_type_name, saga_data_payload::text, steps_snapshot_payload::text, created_at_utc
            FROM {Table}
            WHERE saga_id = @sagaId AND target_version > @afterVersion
            ORDER BY sequence_number ASC
            LIMIT @maxCount;
            """;
        command.Parameters.AddWithValue("sagaId", sagaId);
        command.Parameters.AddWithValue("afterVersion", afterVersion);
        command.Parameters.AddWithValue("maxCount", maxCount);

        var results = new List<SagaJournalEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) results.Add(Read(reader));
        return results;
    }

    /// <inheritdoc />
    public async Task<long> GetLatestVersionAsync(Guid sagaId, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandTimeout = options.CommandTimeoutSeconds;
        command.CommandText = $"SELECT MAX(target_version) FROM {Table} WHERE saga_id = @sagaId;";
        command.Parameters.AddWithValue("sagaId", sagaId);
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result == null || result == DBNull.Value ? 0 : Convert.ToInt64(result);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> EnumerateSagaIdsAsync(Guid? afterSagaId, int maxCount,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandTimeout = options.CommandTimeoutSeconds;
        command.CommandText = $"""
            SELECT DISTINCT saga_id FROM {Table}
            WHERE @afterSagaId IS NULL OR saga_id > @afterSagaId
            ORDER BY saga_id
            LIMIT @maxCount;
            """;
        command.Parameters.Add(new NpgsqlParameter("afterSagaId", NpgsqlDbType.Uuid)
        {
            Value = (object?)afterSagaId ?? DBNull.Value
        });
        command.Parameters.AddWithValue("maxCount", maxCount);

        var results = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) results.Add(reader.GetGuid(0));
        return results;
    }

    private NpgsqlCommand CreateCommand(RelationalConnectionLease<NpgsqlConnection, NpgsqlTransaction> lease, string sql)
    {
        var command = lease.Connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = options.CommandTimeoutSeconds;
        if (lease.Transaction != null) command.Transaction = lease.Transaction;
        return command;
    }

    private static void AddEntryParameters(NpgsqlCommand command, SagaJournalEntry entry)
    {
        command.Parameters.AddWithValue("journalEntryId", entry.JournalEntryId);
        command.Parameters.AddWithValue("transitionId", entry.TransitionId);
        command.Parameters.AddWithValue("sagaId", entry.SagaId);
        command.Parameters.AddWithValue("sequenceNumber", entry.SequenceNumber);
        command.Parameters.AddWithValue("previousVersion", entry.PreviousVersion);
        command.Parameters.AddWithValue("targetVersion", entry.TargetVersion);
        command.Parameters.AddWithValue("messageId", (object?)entry.MessageId ?? DBNull.Value);
        command.Parameters.AddWithValue("requestId", (object?)entry.RequestId ?? DBNull.Value);
        command.Parameters.AddWithValue("correlationId", (object?)entry.CorrelationId ?? DBNull.Value);
        command.Parameters.AddWithValue("causationId", (object?)entry.CausationId ?? DBNull.Value);
        command.Parameters.AddWithValue("parentMessageId", (object?)entry.ParentMessageId ?? DBNull.Value);
        command.Parameters.AddWithValue("applicationId", (object?)entry.ApplicationId ?? DBNull.Value);
        command.Parameters.AddWithValue("handlerType", (object?)entry.HandlerType ?? DBNull.Value);
        command.Parameters.AddWithValue("messageType", (object?)entry.MessageType ?? DBNull.Value);
        command.Parameters.AddWithValue("messageSchemaVersion", entry.MessageSchemaVersion);
        command.Parameters.AddWithValue("journalSchemaVersion", entry.JournalSchemaVersion);
        command.Parameters.AddWithValue("transitionType", (int)entry.TransitionType);
        command.Parameters.AddWithValue("sagaDataTypeName", entry.SagaDataTypeName);
        command.Parameters.Add(new NpgsqlParameter("sagaDataPayload", NpgsqlDbType.Jsonb) { Value = entry.SagaDataPayload });
        command.Parameters.Add(new NpgsqlParameter("stepsSnapshotPayload", NpgsqlDbType.Jsonb)
        {
            Value = (object?)entry.StepsSnapshotPayload ?? DBNull.Value
        });
        command.Parameters.AddWithValue("createdAt", entry.CreatedAtUtc);
    }

    private static SagaJournalEntry Read(NpgsqlDataReader reader) => new()
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
