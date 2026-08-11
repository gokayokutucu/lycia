// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Common.Helpers;
using Lycia.Common.SagaSteps;
using Lycia.Extensions;
using Lycia.Persistence.Relational.Internal.Sessions;
using Lycia.Saga.Abstractions.Inbox;
using Lycia.Saga.Abstractions.Persistence;
using Npgsql;
using NpgsqlTypes;

namespace Lycia.Persistence.PostgreSql;

/// <summary>PostgreSQL backed implementation of <see cref="IInboxStore"/>.</summary>
public class PostgreSqlInboxStore(PostgreSqlInboxOptions options,
    ILyciaPersistenceSessionAccessor? sessionAccessor = null) : IInboxStore
{
    private string InboxTable => options.QualifiedInboxTable;

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

    private static void AddNullableJsonb(NpgsqlCommand command, string name, string? value)
    {
        command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Jsonb)
        {
            Value = (object?)value ?? DBNull.Value
        });
    }

    /// <inheritdoc />
    public async Task<InboxBeginResult> TryBeginAsync(Guid messageId, Type handlerType, CancellationToken cancellationToken = default)
    {
        var handlerTypeName = handlerType.GetSimplifiedQualifiedName();

        await using var lease = await RelationalConnectionLease<NpgsqlConnection, NpgsqlTransaction>.OpenAsync(
            sessionAccessor, CreateConnection, cancellationToken).ConfigureAwait(false);

        using var insert = CreateCommand(lease.Connection, $"""
                INSERT INTO {InboxTable} (message_id, handler_type, status, created_at_utc, updated_at_utc)
                VALUES (@messageId, @handlerType, @status, now(), now())
                ON CONFLICT (message_id, handler_type) DO NOTHING;
                """, lease.Transaction);
        insert.Parameters.AddWithValue("messageId", messageId);
        insert.Parameters.AddWithValue("handlerType", handlerTypeName);
        insert.Parameters.AddWithValue("status", (int)InboxMessageStatus.Processing);
        var inserted = await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (inserted > 0) return InboxBeginResult.Started;

        var status = await SelectStatusAsync(lease.Connection, lease.Transaction, messageId, handlerTypeName,
            cancellationToken).ConfigureAwait(false);
        return status switch
        {
            InboxMessageStatus.Completed => InboxBeginResult.AlreadyCompleted,
            InboxMessageStatus.Failed => InboxBeginResult.AlreadyFailed,
            _ => InboxBeginResult.AlreadyProcessing
        };
    }

    /// <inheritdoc />
    public Task MarkCompletedAsync(Guid messageId, Type handlerType, CancellationToken cancellationToken = default) =>
        UpdateStatusAsync(messageId, handlerType, InboxMessageStatus.Completed, null, cancellationToken);

    /// <inheritdoc />
    public Task MarkFailedAsync(Guid messageId, Type handlerType, SagaStepFailureInfo? failureInfo, CancellationToken cancellationToken = default) =>
        UpdateStatusAsync(messageId, handlerType, InboxMessageStatus.Failed, failureInfo, cancellationToken);

    private async Task UpdateStatusAsync(Guid messageId, Type handlerType, InboxMessageStatus status,
        SagaStepFailureInfo? failureInfo, CancellationToken cancellationToken)
    {
        var handlerTypeName = handlerType.GetSimplifiedQualifiedName();

        await using var lease = await RelationalConnectionLease<NpgsqlConnection, NpgsqlTransaction>.OpenAsync(
            sessionAccessor, CreateConnection, cancellationToken).ConfigureAwait(false);
        using var command = CreateCommand(lease.Connection, $"""
            UPDATE {InboxTable}
            SET status = @status, failure_info_json = @failureInfo, updated_at_utc = now()
            WHERE message_id = @messageId AND handler_type = @handlerType;
            """, lease.Transaction);
        command.Parameters.AddWithValue("messageId", messageId);
        command.Parameters.AddWithValue("handlerType", handlerTypeName);
        command.Parameters.AddWithValue("status", (int)status);
        AddNullableJsonb(command, "failureInfo", failureInfo != null ? JsonHelper.SerializeSafe(failureInfo) : null);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<InboxMessageStatus> GetStatusAsync(Guid messageId, Type handlerType, CancellationToken cancellationToken = default)
    {
        await using var lease = await RelationalConnectionLease<NpgsqlConnection, NpgsqlTransaction>.OpenAsync(
            sessionAccessor, CreateConnection, cancellationToken).ConfigureAwait(false);
        return await SelectStatusAsync(lease.Connection, lease.Transaction, messageId,
                handlerType.GetSimplifiedQualifiedName(), cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<InboxMessageStatus> SelectStatusAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction,
        Guid messageId,
        string handlerTypeName, CancellationToken cancellationToken)
    {
        using var command = CreateCommand(connection, $"""
            SELECT status FROM {InboxTable} WHERE message_id = @messageId AND handler_type = @handlerType;
            """, transaction);
        command.Parameters.AddWithValue("messageId", messageId);
        command.Parameters.AddWithValue("handlerType", handlerTypeName);

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result == null ? InboxMessageStatus.None : (InboxMessageStatus)(int)result;
    }
}
