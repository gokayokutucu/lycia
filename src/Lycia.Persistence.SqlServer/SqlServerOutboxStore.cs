// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Common.Helpers;
using Lycia.Common.SagaSteps;
using Lycia.Persistence.Relational.Internal.Sessions;
using Lycia.Saga.Abstractions.Outbox;
using Lycia.Saga.Abstractions.Persistence;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json;

namespace Lycia.Persistence.SqlServer;

/// <summary>Microsoft SQL Server backed implementation of <see cref="IOutboxStore"/>.</summary>
public class SqlServerOutboxStore(SqlServerOutboxOptions options,
    ILyciaPersistenceSessionAccessor? sessionAccessor = null) : IOutboxStore
{
    private string OutboxTable => options.OutboxTable;

    private SqlConnection CreateConnection() => new(options.ConnectionString);

    private SqlCommand CreateCommand(SqlConnection connection, string sql, SqlTransaction? transaction = null)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = options.CommandTimeoutSeconds;
        if (transaction != null) command.Transaction = transaction;
        return command;
    }

    /// <inheritdoc />
    public async Task AddAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        if (message is null) throw new ArgumentNullException(nameof(message));

        await using var lease = await RelationalConnectionLease<SqlConnection, SqlTransaction>.OpenAsync(
            sessionAccessor, CreateConnection, cancellationToken).ConfigureAwait(false);
        using var insert = CreateCommand(lease.Connection, $"""
                IF NOT EXISTS (SELECT 1 FROM {OutboxTable} WITH (UPDLOCK, HOLDLOCK) WHERE MessageId = @messageId)
                BEGIN
                INSERT INTO {OutboxTable}
                    (MessageId, MessageTypeName, Payload, ApplicationId, SagaId, Status, RetryCount, CreatedAtUtc, UpdatedAtUtc)
                VALUES
                    (@messageId, @messageTypeName, @payload, @applicationId, @sagaId, @status, 0, @createdAt, @updatedAt);
                END
                """, lease.Transaction);
        insert.Parameters.AddWithValue("@messageId", message.MessageId);
        insert.Parameters.AddWithValue("@messageTypeName", message.MessageTypeName);
        insert.Parameters.AddWithValue("@payload", message.Payload);
        insert.Parameters.AddWithValue("@applicationId", (object?)message.ApplicationId ?? DBNull.Value);
        insert.Parameters.AddWithValue("@sagaId", (object?)message.SagaId ?? DBNull.Value);
        insert.Parameters.AddWithValue("@status", (int)message.Status);
        insert.Parameters.AddWithValue("@createdAt", message.CreatedAtUtc);
        insert.Parameters.AddWithValue("@updatedAt", message.UpdatedAtUtc);
        await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<OutboxMessage?> GetByMessageIdAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        await using var lease = await RelationalConnectionLease<SqlConnection, SqlTransaction>.OpenAsync(
            sessionAccessor, CreateConnection, cancellationToken).ConfigureAwait(false);
        using var command = CreateCommand(lease.Connection, $"""
            SELECT MessageId, MessageTypeName, Payload, ApplicationId, SagaId, Status, FailureInfoJson, RetryCount, CreatedAtUtc, UpdatedAtUtc
            FROM {OutboxTable}
            WHERE MessageId = @messageId;
            """, lease.Transaction);
        command.Parameters.AddWithValue("@messageId", messageId);

        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;

        return MapRowToMessage(reader);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutboxMessage>> ClaimPendingBatchAsync(int maxCount,
        CancellationToken cancellationToken = default, int maxAttempts = 5, TimeSpan? recoveryTimeout = null)
    {
        await using var lease = await RelationalConnectionLease<SqlConnection, SqlTransaction>.OpenAsync(
            sessionAccessor, CreateConnection, cancellationToken).ConfigureAwait(false);

        // The derived-table TOP/ORDER BY claims the oldest pending rows and takes row locks on exactly
        // those rows before flipping Status; READPAST makes a concurrent claimer skip rows already
        // locked by another caller instead of blocking or double-claiming them.
        using var command = CreateCommand(lease.Connection, $"""
            UPDATE claimed
            SET Status = @claimedStatus, UpdatedAtUtc = SYSUTCDATETIME()
            OUTPUT INSERTED.MessageId, INSERTED.MessageTypeName, INSERTED.Payload, INSERTED.ApplicationId,
                   INSERTED.SagaId, INSERTED.Status, INSERTED.FailureInfoJson, INSERTED.RetryCount,
                   INSERTED.CreatedAtUtc, INSERTED.UpdatedAtUtc
            FROM (
                SELECT TOP (@maxCount) *
                FROM {OutboxTable} WITH (ROWLOCK, READPAST)
                WHERE (Status = @pendingStatus OR Status = @unknownStatus OR
                      ((Status = @claimedStatus OR Status = @publishingStatus) AND UpdatedAtUtc <= @staleBefore))
                  AND RetryCount < @maxAttempts
                ORDER BY CreatedAtUtc
            ) AS claimed;
            """, lease.Transaction);
        command.Parameters.AddWithValue("@maxCount", maxCount);
        command.Parameters.AddWithValue("@pendingStatus", (int)OutboxMessageStatus.Pending);
        command.Parameters.AddWithValue("@unknownStatus", (int)OutboxMessageStatus.ConfirmationUnknown);
        command.Parameters.AddWithValue("@claimedStatus", (int)OutboxMessageStatus.Claimed);
        command.Parameters.AddWithValue("@publishingStatus", (int)OutboxMessageStatus.Publishing);
        command.Parameters.AddWithValue("@maxAttempts", maxAttempts);
        command.Parameters.AddWithValue("@staleBefore",
            DateTime.UtcNow.Subtract(recoveryTimeout ?? TimeSpan.FromMinutes(1)));

        var claimed = new List<OutboxMessage>();
        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            claimed.Add(MapRowToMessage(reader));
        }

        return claimed;
    }

    private static OutboxMessage MapRowToMessage(SqlDataReader reader)
    {
        var messageId = reader.GetGuid(0);
        var messageTypeName = reader.GetString(1);
        var payload = reader.GetString(2);
        var applicationId = reader.IsDBNull(3) ? null : reader.GetString(3);
        var sagaId = reader.IsDBNull(4) ? (Guid?)null : reader.GetGuid(4);
        var status = (OutboxMessageStatus)reader.GetInt32(5);
        var failureInfoJson = reader.IsDBNull(6) ? null : reader.GetString(6);
        var retryCount = reader.GetInt32(7);
        var updatedAtUtc = reader.GetDateTime(9);

        var message = new OutboxMessage(messageId, messageTypeName, payload, applicationId, sagaId)
        {
            Status = status,
            RetryCount = retryCount,
            FailureInfo = failureInfoJson == null ? null : JsonConvert.DeserializeObject<SagaStepFailureInfo>(failureInfoJson),
            UpdatedAtUtc = updatedAtUtc
        };

        return message;
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
        await using var lease = await RelationalConnectionLease<SqlConnection, SqlTransaction>.OpenAsync(
            sessionAccessor, CreateConnection, cancellationToken).ConfigureAwait(false);
        using var command = CreateCommand(lease.Connection, $"""
            UPDATE {OutboxTable}
            SET Status = @status, FailureInfoJson = @failureInfo,
                RetryCount = RetryCount + @retryIncrement, UpdatedAtUtc = SYSUTCDATETIME()
            WHERE MessageId = @messageId;
            """, lease.Transaction);
        command.Parameters.AddWithValue("@messageId", messageId);
        command.Parameters.AddWithValue("@status", (int)status);
        command.Parameters.AddWithValue("@retryIncrement", incrementRetry ? 1 : 0);
        command.Parameters.AddWithValue("@failureInfo",
            (object?)(failureInfo != null ? JsonHelper.SerializeSafe(failureInfo) : null) ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
