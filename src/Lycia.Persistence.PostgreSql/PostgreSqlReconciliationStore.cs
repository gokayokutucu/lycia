// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
using Lycia.Persistence.Relational.Internal.Sessions;
using Lycia.Saga.Abstractions.Persistence;
using Lycia.Saga.Abstractions.Persistence.Reconciliation;
using Npgsql;
using NpgsqlTypes;

namespace Lycia.Persistence.PostgreSql;

/// <summary>PostgreSQL durable canonical store for Split Store projection intents.</summary>
public sealed class PostgreSqlReconciliationStore(
    PostgreSqlSagaStoreOptions options,
    ILyciaPersistenceSessionAccessor? sessionAccessor) : IReconciliationStore
{
    private string Table => $"\"{options.SchemaName}\".lycia_saga_reconciliation";
    private NpgsqlConnection CreateConnection() => new(options.BuildEffectiveConnectionString());

    /// <inheritdoc />
    public async Task AddAsync(SagaProjectionIntent intent, CancellationToken cancellationToken = default)
    {
        await using var lease = await RelationalConnectionLease<NpgsqlConnection, NpgsqlTransaction>.OpenAsync(
            sessionAccessor, CreateConnection).ConfigureAwait(false);
        using var command = CreateCommand(lease, $"""
            INSERT INTO {Table} (transition_id, saga_id, message_id, expected_version, target_version,
                saga_data_type, payload, status, created_at_utc, updated_at_utc)
            VALUES (@transitionId, @sagaId, @messageId, @expectedVersion, @targetVersion,
                @dataType, @payload, @status, @createdAt, @createdAt)
            ON CONFLICT (transition_id) DO NOTHING;
            """);
        AddIntentParameters(command, intent);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SagaProjectionIntent>> ClaimAsync(string workerId, int batchSize, int maxAttempts,
        TimeSpan claimTimeout, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandTimeout = options.CommandTimeoutSeconds;
        command.CommandText = $"""
            WITH candidates AS (
                SELECT transition_id FROM {Table}
                WHERE attempt_count < @maxAttempts AND (
                    (status IN (@pending, @retryPending) AND (next_attempt_at_utc IS NULL OR next_attempt_at_utc <= now()))
                    OR (status = @claimed AND claimed_at_utc < now() - @claimTimeout))
                ORDER BY saga_id, target_version
                FOR UPDATE SKIP LOCKED
                LIMIT @batchSize
            )
            UPDATE {Table} AS r
            SET status = @claimed, worker_id = @workerId, claimed_at_utc = now(),
                last_attempt_at_utc = now(), attempt_count = attempt_count + 1, updated_at_utc = now()
            FROM candidates c
            WHERE r.transition_id = c.transition_id
            RETURNING r.transition_id, r.saga_id, r.message_id, r.expected_version, r.target_version,
                r.saga_data_type, r.payload::text, r.status, r.attempt_count, r.created_at_utc, r.next_attempt_at_utc;
            """;
        command.Parameters.AddWithValue("maxAttempts", maxAttempts);
        command.Parameters.AddWithValue("pending", (int)ReconciliationStatus.Pending);
        command.Parameters.AddWithValue("retryPending", (int)ReconciliationStatus.RetryPending);
        command.Parameters.AddWithValue("claimed", (int)ReconciliationStatus.Claimed);
        command.Parameters.AddWithValue("claimTimeout", claimTimeout);
        command.Parameters.AddWithValue("batchSize", batchSize);
        command.Parameters.AddWithValue("workerId", workerId);
        var results = new List<SagaProjectionIntent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) results.Add(Read(reader));
        return results;
    }

    /// <inheritdoc />
    public Task MarkCompletedAsync(Guid transitionId, ReconciliationStatus status,
        CancellationToken cancellationToken = default) => UpdateStatusAsync(transitionId, status, null, null, cancellationToken);

    /// <inheritdoc />
    public Task MarkRetryAsync(Guid transitionId, DateTime nextAttemptAtUtc, string failureCode,
        CancellationToken cancellationToken = default) =>
        UpdateStatusAsync(transitionId, ReconciliationStatus.RetryPending, nextAttemptAtUtc, failureCode, cancellationToken);

    /// <inheritdoc />
    public Task MarkFailedAsync(Guid transitionId, string failureCode, CancellationToken cancellationToken = default) =>
        UpdateStatusAsync(transitionId, ReconciliationStatus.Failed, null, failureCode, cancellationToken);

    /// <inheritdoc />
    public async Task<bool> QueueLatestAsync(Guid sagaId, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {Table} SET status = @pending, attempt_count = 0, next_attempt_at_utc = NULL,
                worker_id = NULL, claimed_at_utc = NULL, failure_code = NULL, updated_at_utc = now()
            WHERE transition_id = (SELECT transition_id FROM {Table} WHERE saga_id = @sagaId
                ORDER BY target_version DESC LIMIT 1);
            """;
        command.Parameters.AddWithValue("pending", (int)ReconciliationStatus.Pending);
        command.Parameters.AddWithValue("sagaId", sagaId);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
    }

    private async Task UpdateStatusAsync(Guid transitionId, ReconciliationStatus status, DateTime? next,
        string? failureCode, CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = $"UPDATE {Table} SET status=@status, next_attempt_at_utc=@next, failure_code=@failure, worker_id=NULL, claimed_at_utc=NULL, updated_at_utc=now() WHERE transition_id=@id;";
        command.Parameters.AddWithValue("status", (int)status);
        command.Parameters.AddWithValue("next", (object?)next ?? DBNull.Value);
        command.Parameters.AddWithValue("failure", (object?)failureCode ?? DBNull.Value);
        command.Parameters.AddWithValue("id", transitionId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private NpgsqlCommand CreateCommand(RelationalConnectionLease<NpgsqlConnection, NpgsqlTransaction> lease, string sql)
    {
        var command = lease.Connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = options.CommandTimeoutSeconds;
        if (lease.Transaction != null) command.Transaction = lease.Transaction;
        return command;
    }

    private static void AddIntentParameters(NpgsqlCommand command, SagaProjectionIntent intent)
    {
        command.Parameters.AddWithValue("transitionId", intent.TransitionId);
        command.Parameters.AddWithValue("sagaId", intent.SagaId);
        command.Parameters.AddWithValue("messageId", (object?)intent.MessageId ?? DBNull.Value);
        command.Parameters.AddWithValue("expectedVersion", intent.ExpectedVersion);
        command.Parameters.AddWithValue("targetVersion", intent.TargetVersion);
        command.Parameters.AddWithValue("dataType", intent.SagaDataType);
        command.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Jsonb) { Value = intent.Payload });
        command.Parameters.AddWithValue("status", (int)intent.Status);
        command.Parameters.AddWithValue("createdAt", intent.CreatedAtUtc);
    }

    private static SagaProjectionIntent Read(NpgsqlDataReader reader) => new()
    {
        TransitionId = reader.GetGuid(0), SagaId = reader.GetGuid(1),
        MessageId = reader.IsDBNull(2) ? null : reader.GetGuid(2), ExpectedVersion = reader.GetInt64(3),
        TargetVersion = reader.GetInt64(4), SagaDataType = reader.GetString(5), Payload = reader.GetString(6),
        Status = (ReconciliationStatus)reader.GetInt32(7), AttemptCount = reader.GetInt32(8),
        CreatedAtUtc = reader.GetDateTime(9), NextAttemptAtUtc = reader.IsDBNull(10) ? null : reader.GetDateTime(10)
    };
}
