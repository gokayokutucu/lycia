// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Common.Helpers;
using Lycia.Common.SagaSteps;
using Lycia.Extensions;
using Lycia.Saga.Abstractions.Inbox;
using Npgsql;
using NpgsqlTypes;

namespace Lycia.Persistence.PostgreSql;

/// <summary>PostgreSQL backed implementation of <see cref="IInboxStore"/>.</summary>
public class PostgreSqlInboxStore(PostgreSqlInboxOptions options) : IInboxStore
{
    private const string UniqueViolationSqlState = "23505";
    private const string InboxTable = PostgreSqlInboxOptions.InboxTable;

    private readonly string _connectionString = options.BuildEffectiveConnectionString();

    private NpgsqlConnection CreateConnection() => new(_connectionString);

    private NpgsqlCommand CreateCommand(NpgsqlConnection connection, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = options.CommandTimeoutSeconds;
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

        using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            using var insert = CreateCommand(connection, $"""
                INSERT INTO {InboxTable} (message_id, handler_type, status, created_at_utc, updated_at_utc)
                VALUES (@messageId, @handlerType, @status, now(), now());
                """);
            insert.Parameters.AddWithValue("messageId", messageId);
            insert.Parameters.AddWithValue("handlerType", handlerTypeName);
            insert.Parameters.AddWithValue("status", (int)InboxMessageStatus.Processing);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            return InboxBeginResult.Started;
        }
        catch (PostgresException ex) when (ex.SqlState == UniqueViolationSqlState)
        {
            var status = await SelectStatusAsync(connection, messageId, handlerTypeName, cancellationToken).ConfigureAwait(false);
            return status switch
            {
                InboxMessageStatus.Completed => InboxBeginResult.AlreadyCompleted,
                InboxMessageStatus.Failed => InboxBeginResult.AlreadyFailed,
                _ => InboxBeginResult.AlreadyProcessing
            };
        }
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

        using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var command = CreateCommand(connection, $"""
            UPDATE {InboxTable}
            SET status = @status, failure_info_json = @failureInfo, updated_at_utc = now()
            WHERE message_id = @messageId AND handler_type = @handlerType;
            """);
        command.Parameters.AddWithValue("messageId", messageId);
        command.Parameters.AddWithValue("handlerType", handlerTypeName);
        command.Parameters.AddWithValue("status", (int)status);
        AddNullableJsonb(command, "failureInfo", failureInfo != null ? JsonHelper.SerializeSafe(failureInfo) : null);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<InboxMessageStatus> GetStatusAsync(Guid messageId, Type handlerType, CancellationToken cancellationToken = default)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await SelectStatusAsync(connection, messageId, handlerType.GetSimplifiedQualifiedName(), cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<InboxMessageStatus> SelectStatusAsync(NpgsqlConnection connection, Guid messageId,
        string handlerTypeName, CancellationToken cancellationToken)
    {
        using var command = CreateCommand(connection, $"""
            SELECT status FROM {InboxTable} WHERE message_id = @messageId AND handler_type = @handlerType;
            """);
        command.Parameters.AddWithValue("messageId", messageId);
        command.Parameters.AddWithValue("handlerType", handlerTypeName);

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result == null ? InboxMessageStatus.None : (InboxMessageStatus)(int)result;
    }
}
