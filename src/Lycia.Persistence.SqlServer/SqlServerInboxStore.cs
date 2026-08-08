// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using System.Linq;
using Lycia.Common.SagaSteps;
using Lycia.Common.Helpers;
using Lycia.Extensions;
using Lycia.Saga.Abstractions.Inbox;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json;

namespace Lycia.Persistence.SqlServer;

/// <summary>Microsoft SQL Server backed implementation of <see cref="IInboxStore"/>.</summary>
public class SqlServerInboxStore(SqlServerInboxOptions options) : IInboxStore
{
    private const int UniqueOrPrimaryKeyViolation1 = 2627;
    private const int UniqueOrPrimaryKeyViolation2 = 2601;

    private string InboxTable => options.InboxTable;

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
    public async Task<InboxBeginResult> TryBeginAsync(Guid messageId, Type handlerType, CancellationToken cancellationToken = default)
    {
        var handlerTypeName = handlerType.GetSimplifiedQualifiedName();

        using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            using var insert = CreateCommand(connection, $"""
                INSERT INTO {InboxTable} (MessageId, HandlerType, Status, CreatedAtUtc, UpdatedAtUtc)
                VALUES (@messageId, @handlerType, @status, SYSUTCDATETIME(), SYSUTCDATETIME());
                """);
            insert.Parameters.AddWithValue("@messageId", messageId);
            insert.Parameters.AddWithValue("@handlerType", handlerTypeName);
            insert.Parameters.AddWithValue("@status", (int)InboxMessageStatus.Processing);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            return InboxBeginResult.Started;
        }
        catch (SqlException ex) when (IsUniqueViolation(ex))
        {
            var existingStatus = await SelectStatusAsync(connection, messageId, handlerTypeName, cancellationToken)
                .ConfigureAwait(false);

            return existingStatus switch
            {
                InboxMessageStatus.Processing => InboxBeginResult.AlreadyProcessing,
                InboxMessageStatus.Completed => InboxBeginResult.AlreadyCompleted,
                InboxMessageStatus.Failed => InboxBeginResult.AlreadyFailed,
                _ => InboxBeginResult.Started
            };
        }
    }

    /// <inheritdoc />
    public Task MarkCompletedAsync(Guid messageId, Type handlerType, CancellationToken cancellationToken = default) =>
        UpdateStatusAsync(messageId, handlerType, InboxMessageStatus.Completed, null, cancellationToken);

    /// <inheritdoc />
    public Task MarkFailedAsync(Guid messageId, Type handlerType, SagaStepFailureInfo? failureInfo,
        CancellationToken cancellationToken = default) =>
        UpdateStatusAsync(messageId, handlerType, InboxMessageStatus.Failed, failureInfo, cancellationToken);

    private async Task UpdateStatusAsync(Guid messageId, Type handlerType, InboxMessageStatus status,
        SagaStepFailureInfo? failureInfo, CancellationToken cancellationToken)
    {
        var handlerTypeName = handlerType.GetSimplifiedQualifiedName();

        using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var command = CreateCommand(connection, $"""
            UPDATE {InboxTable}
            SET Status = @status, FailureInfoJson = @failureInfo, UpdatedAtUtc = SYSUTCDATETIME()
            WHERE MessageId = @messageId AND HandlerType = @handlerType;
            """);
        command.Parameters.AddWithValue("@messageId", messageId);
        command.Parameters.AddWithValue("@handlerType", handlerTypeName);
        command.Parameters.AddWithValue("@status", (int)status);
        command.Parameters.AddWithValue("@failureInfo",
            (object?)(failureInfo != null ? JsonHelper.SerializeSafe(failureInfo) : null) ?? DBNull.Value);
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

    private async Task<InboxMessageStatus> SelectStatusAsync(SqlConnection connection, Guid messageId, string handlerTypeName,
        CancellationToken cancellationToken)
    {
        using var command = CreateCommand(connection, $"""
            SELECT Status FROM {InboxTable} WHERE MessageId = @messageId AND HandlerType = @handlerType;
            """);
        command.Parameters.AddWithValue("@messageId", messageId);
        command.Parameters.AddWithValue("@handlerType", handlerTypeName);

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result == null ? InboxMessageStatus.None : (InboxMessageStatus)(int)result;
    }
}
