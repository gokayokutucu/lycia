// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using System.Linq;
using Lycia.Common.Helpers;
using Lycia.Common.SagaSteps;
using Lycia.Saga.Abstractions.Outbox;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json;

namespace Lycia.Persistence.SqlServer;

/// <summary>Microsoft SQL Server backed implementation of <see cref="IOutboxStore"/>.</summary>
public class SqlServerOutboxStore(SqlServerOutboxOptions options) : IOutboxStore
{
    private const int UniqueOrPrimaryKeyViolation1 = 2627;
    private const int UniqueOrPrimaryKeyViolation2 = 2601;

    private string OutboxTable => options.OutboxTable;

    private SqlConnection CreateConnection() => new(options.ConnectionString);

    private SqlCommand CreateCommand(SqlConnection connection, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = options.CommandTimeoutSeconds;
        return command;
    }

    private static bool IsUniqueViolation(SqlException ex) =>
        ex.Errors.Cast<SqlError>().Any(e => e.Number is UniqueOrPrimaryKeyViolation1 or UniqueOrPrimaryKeyViolation2);

    /// <inheritdoc />
    public async Task AddAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        if (message is null) throw new ArgumentNullException(nameof(message));

        using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            using var insert = CreateCommand(connection, $"""
                INSERT INTO {OutboxTable}
                    (MessageId, MessageTypeName, Payload, ApplicationId, SagaId, Status, RetryCount, CreatedAtUtc, UpdatedAtUtc)
                VALUES
                    (@messageId, @messageTypeName, @payload, @applicationId, @sagaId, @status, 0, @createdAt, @updatedAt);
                """);
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
        catch (SqlException ex) when (IsUniqueViolation(ex))
        {
            // Already captured; re-adding must not reset an already-advanced status.
        }
    }

    /// <inheritdoc />
    public async Task<OutboxMessage?> GetByMessageIdAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var command = CreateCommand(connection, $"""
            SELECT MessageId, MessageTypeName, Payload, ApplicationId, SagaId, Status, FailureInfoJson, CreatedAtUtc, UpdatedAtUtc
            FROM {OutboxTable}
            WHERE MessageId = @messageId;
            """);
        command.Parameters.AddWithValue("@messageId", messageId);

        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;

        return MapRowToMessage(reader);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutboxMessage>> ClaimPendingBatchAsync(int maxCount, CancellationToken cancellationToken = default)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        // The derived-table TOP/ORDER BY claims the oldest pending rows and takes row locks on exactly
        // those rows before flipping Status; READPAST makes a concurrent claimer skip rows already
        // locked by another caller instead of blocking or double-claiming them.
        using var command = CreateCommand(connection, $"""
            UPDATE claimed
            SET Status = @claimedStatus, UpdatedAtUtc = SYSUTCDATETIME()
            OUTPUT INSERTED.MessageId, INSERTED.MessageTypeName, INSERTED.Payload, INSERTED.ApplicationId,
                   INSERTED.SagaId, INSERTED.Status, INSERTED.FailureInfoJson, INSERTED.CreatedAtUtc, INSERTED.UpdatedAtUtc
            FROM (
                SELECT TOP (@maxCount) *
                FROM {OutboxTable} WITH (ROWLOCK, READPAST)
                WHERE Status = @pendingStatus
                ORDER BY CreatedAtUtc
            ) AS claimed;
            """);
        command.Parameters.AddWithValue("@maxCount", maxCount);
        command.Parameters.AddWithValue("@pendingStatus", (int)OutboxMessageStatus.Pending);
        command.Parameters.AddWithValue("@claimedStatus", (int)OutboxMessageStatus.Claimed);

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
        var updatedAtUtc = reader.GetDateTime(8);

        var message = new OutboxMessage(messageId, messageTypeName, payload, applicationId, sagaId)
        {
            Status = status,
            FailureInfo = failureInfoJson == null ? null : JsonConvert.DeserializeObject<SagaStepFailureInfo>(failureInfoJson),
            UpdatedAtUtc = updatedAtUtc
        };

        return message;
    }

    /// <inheritdoc />
    public Task MarkPublishingAsync(Guid messageId, CancellationToken cancellationToken = default) =>
        UpdateStatusAsync(messageId, OutboxMessageStatus.Publishing, null, cancellationToken);

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
        CancellationToken cancellationToken)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var command = CreateCommand(connection, $"""
            UPDATE {OutboxTable}
            SET Status = @status, FailureInfoJson = @failureInfo, UpdatedAtUtc = SYSUTCDATETIME()
            WHERE MessageId = @messageId;
            """);
        command.Parameters.AddWithValue("@messageId", messageId);
        command.Parameters.AddWithValue("@status", (int)status);
        command.Parameters.AddWithValue("@failureInfo",
            (object?)(failureInfo != null ? JsonHelper.SerializeSafe(failureInfo) : null) ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
