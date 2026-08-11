// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Common.Helpers;
using Lycia.Common.SagaSteps;
using Lycia.Saga.Abstractions.Outbox;
using Newtonsoft.Json;
using Npgsql;
using NpgsqlTypes;

namespace Lycia.Persistence.PostgreSql;

/// <summary>PostgreSQL backed implementation of <see cref="IOutboxStore"/>.</summary>
public class PostgreSqlOutboxStore(PostgreSqlOutboxOptions options) : IOutboxStore
{
    private const string OutboxTable = PostgreSqlOutboxOptions.OutboxTable;

    private static readonly string SelectColumns =
        "message_id, message_type_name, payload, application_id, saga_id, status, retry_count, failure_info_json, created_at_utc, updated_at_utc";

    private readonly string _connectionString = options.BuildEffectiveConnectionString();

    private NpgsqlConnection CreateConnection() => new(_connectionString);

    private NpgsqlCommand CreateCommand(NpgsqlConnection connection, string sql, NpgsqlTransaction? transaction = null)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = options.CommandTimeoutSeconds;
        if (transaction != null) command.Transaction = transaction;
        return command;
    }

    private static void AddJsonb(NpgsqlCommand command, string name, string value)
    {
        command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Jsonb) { Value = value });
    }

    private static void AddNullableJsonb(NpgsqlCommand command, string name, string? value)
    {
        command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Jsonb)
        {
            Value = (object?)value ?? DBNull.Value
        });
    }

    /// <inheritdoc />
    public async Task AddAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        if (message is null) throw new ArgumentNullException(nameof(message));

        using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var command = CreateCommand(connection, $"""
            INSERT INTO {OutboxTable}
                (message_id, message_type_name, payload, application_id, saga_id, status, retry_count, failure_info_json, created_at_utc, updated_at_utc)
            VALUES
                (@messageId, @messageTypeName, @payload, @applicationId, @sagaId, @status, 0, NULL, @createdAt, @updatedAt)
            ON CONFLICT (message_id) DO NOTHING;
            """);
        command.Parameters.AddWithValue("messageId", message.MessageId);
        command.Parameters.AddWithValue("messageTypeName", message.MessageTypeName);
        AddJsonb(command, "payload", message.Payload);
        command.Parameters.AddWithValue("applicationId", (object?)message.ApplicationId ?? DBNull.Value);
        command.Parameters.AddWithValue("sagaId", (object?)message.SagaId ?? DBNull.Value);
        command.Parameters.AddWithValue("status", (int)message.Status);
        command.Parameters.AddWithValue("createdAt", message.CreatedAtUtc);
        command.Parameters.AddWithValue("updatedAt", message.UpdatedAtUtc);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<OutboxMessage?> GetByMessageIdAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var command = CreateCommand(connection, $"""
            SELECT {SelectColumns} FROM {OutboxTable} WHERE message_id = @messageId;
            """);
        command.Parameters.AddWithValue("messageId", messageId);

        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;

        return MapRow(reader);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutboxMessage>> ClaimPendingBatchAsync(int maxCount,
        CancellationToken cancellationToken = default, int maxAttempts = 5, TimeSpan? recoveryTimeout = null)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var command = CreateCommand(connection, $"""
            UPDATE {OutboxTable}
            SET status = @claimedStatus, updated_at_utc = now()
            WHERE message_id IN (
                SELECT message_id FROM {OutboxTable}
                WHERE (status = @pendingStatus OR status = @unknownStatus OR
                      ((status = @claimedStatus OR status = @publishingStatus) AND updated_at_utc <= @staleBefore))
                  AND retry_count < @maxAttempts
                ORDER BY created_at_utc
                LIMIT @maxCount
                FOR UPDATE SKIP LOCKED
            )
            RETURNING {SelectColumns};
            """);
        command.Parameters.AddWithValue("claimedStatus", (int)OutboxMessageStatus.Claimed);
        command.Parameters.AddWithValue("pendingStatus", (int)OutboxMessageStatus.Pending);
        command.Parameters.AddWithValue("unknownStatus", (int)OutboxMessageStatus.ConfirmationUnknown);
        command.Parameters.AddWithValue("publishingStatus", (int)OutboxMessageStatus.Publishing);
        command.Parameters.AddWithValue("maxCount", maxCount);
        command.Parameters.AddWithValue("maxAttempts", maxAttempts);
        command.Parameters.AddWithValue("staleBefore",
            DateTime.UtcNow.Subtract(recoveryTimeout ?? TimeSpan.FromMinutes(1)));

        var claimed = new List<OutboxMessage>();
        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            claimed.Add(MapRow(reader));
        }

        return claimed;
    }

    /// <inheritdoc />
    public Task MarkPublishingAsync(Guid messageId, CancellationToken cancellationToken = default) =>
        UpdateStatusAsync(messageId, OutboxMessageStatus.Publishing, null, cancellationToken, incrementRetry: true);

    /// <inheritdoc />
    public Task MarkPublishedAsync(Guid messageId, CancellationToken cancellationToken = default) =>
        UpdateStatusAsync(messageId, OutboxMessageStatus.Published, null, cancellationToken);

    /// <inheritdoc />
    public Task MarkConfirmationUnknownAsync(Guid messageId, CancellationToken cancellationToken = default) =>
        UpdateStatusAsync(messageId, OutboxMessageStatus.ConfirmationUnknown, null, cancellationToken);

    /// <inheritdoc />
    public Task MarkFailedAsync(Guid messageId, SagaStepFailureInfo? failureInfo, CancellationToken cancellationToken = default) =>
        UpdateStatusAsync(messageId, OutboxMessageStatus.Failed, failureInfo, cancellationToken);

    private async Task UpdateStatusAsync(Guid messageId, OutboxMessageStatus status, SagaStepFailureInfo? failureInfo,
        CancellationToken cancellationToken, bool incrementRetry = false)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var command = CreateCommand(connection, $"""
            UPDATE {OutboxTable}
            SET status = @status, failure_info_json = COALESCE(@failureInfo, failure_info_json),
                retry_count = retry_count + @retryIncrement, updated_at_utc = now()
            WHERE message_id = @messageId;
            """);
        command.Parameters.AddWithValue("messageId", messageId);
        command.Parameters.AddWithValue("status", (int)status);
        command.Parameters.AddWithValue("retryIncrement", incrementRetry ? 1 : 0);
        AddNullableJsonb(command, "failureInfo", failureInfo != null ? JsonHelper.SerializeSafe(failureInfo) : null);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static OutboxMessage MapRow(NpgsqlDataReader reader)
    {
        var messageId = reader.GetGuid(0);
        var messageTypeName = reader.GetString(1);
        var payload = reader.GetString(2);
        var applicationId = reader.IsDBNull(3) ? null : reader.GetString(3);
        var sagaId = reader.IsDBNull(4) ? (Guid?)null : reader.GetGuid(4);
        var status = (OutboxMessageStatus)reader.GetInt32(5);
        var failureInfoJson = reader.IsDBNull(7) ? null : reader.GetString(7);
        var createdAtUtc = reader.GetDateTime(8);
        var updatedAtUtc = reader.GetDateTime(9);

        var message = new OutboxMessage(messageId, messageTypeName, payload, applicationId, sagaId)
        {
            Status = status,
            RetryCount = reader.GetInt32(6),
            FailureInfo = failureInfoJson == null ? null : JsonConvert.DeserializeObject<SagaStepFailureInfo>(failureInfoJson),
            UpdatedAtUtc = updatedAtUtc
        };

        // CreatedAtUtc is init-only on OutboxMessage (set from DateTime.UtcNow in the ctor); the durable
        // row's real created_at_utc is only used for ordering here, not round-tripped onto the instance.
        _ = createdAtUtc;

        return message;
    }
}
