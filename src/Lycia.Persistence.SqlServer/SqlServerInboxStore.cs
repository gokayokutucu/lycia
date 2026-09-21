// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using System.Linq;
using Lycia.Common.SagaSteps;
using Lycia.Common.Helpers;
using Lycia.Extensions;
using Lycia.Persistence.Relational.Internal.Sessions;
using Lycia.Saga.Abstractions.Inbox;
using Lycia.Saga.Abstractions.Persistence;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json;

namespace Lycia.Persistence.SqlServer;

/// <summary>Microsoft SQL Server backed implementation of <see cref="IInboxStore"/>.</summary>
public class SqlServerInboxStore(SqlServerInboxOptions options,
    ILyciaPersistenceSessionAccessor? sessionAccessor = null) : IInboxStore
{
    private const int UniqueOrPrimaryKeyViolation1 = 2627;
    private const int UniqueOrPrimaryKeyViolation2 = 2601;

    private string InboxTable => options.InboxTable;

    private SqlConnection CreateConnection() => new(options.ConnectionString);

    private SqlCommand CreateCommand(SqlConnection connection, string sql, SqlTransaction? transaction = null)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = options.CommandTimeoutSeconds;
        if (transaction != null) command.Transaction = transaction;
        return command;
    }

    private static bool IsUniqueViolation(SqlException ex) =>
        ex.Errors.Cast<SqlError>().Any(e => e.Number is UniqueOrPrimaryKeyViolation1 or UniqueOrPrimaryKeyViolation2);

    /// <inheritdoc />
    public async Task<InboxBeginResult> TryBeginAsync(Guid messageId, Type handlerType, CancellationToken cancellationToken = default)
    {
        var handlerTypeName = handlerType.GetSimplifiedQualifiedName();

        await using var lease = await RelationalConnectionLease<SqlConnection, SqlTransaction>.OpenAsync(
            sessionAccessor, CreateConnection, cancellationToken).ConfigureAwait(false);

        var existingStatus = await SelectStatusAsync(lease.Connection, lease.Transaction, messageId, handlerTypeName,
            cancellationToken, lockForInsert: lease.Transaction != null).ConfigureAwait(false);
        if (existingStatus != InboxMessageStatus.None)
            return await ResolveExistingAsync(lease, messageId, handlerTypeName, existingStatus, cancellationToken)
                .ConfigureAwait(false);

        try
        {
            using var insert = CreateCommand(lease.Connection, $"""
                INSERT INTO {InboxTable} (MessageId, HandlerType, Status, CreatedAtUtc, UpdatedAtUtc)
                VALUES (@messageId, @handlerType, @status, SYSUTCDATETIME(), SYSUTCDATETIME());
                """, lease.Transaction);
            insert.Parameters.AddWithValue("@messageId", messageId);
            insert.Parameters.AddWithValue("@handlerType", handlerTypeName);
            insert.Parameters.AddWithValue("@status", (int)InboxMessageStatus.Processing);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            return InboxBeginResult.Started;
        }
        catch (SqlException ex) when (IsUniqueViolation(ex))
        {
            existingStatus = await SelectStatusAsync(lease.Connection, lease.Transaction, messageId, handlerTypeName,
                    cancellationToken)
                .ConfigureAwait(false);

            if (existingStatus == InboxMessageStatus.None) return InboxBeginResult.Started;
            return await ResolveExistingAsync(lease, messageId, handlerTypeName, existingStatus, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    // Decides what an already-present record means for this delivery. Completed is an ordinary duplicate
    // and stays suppressed. Anything else may be a claim stranded by a dead process, or a failed attempt
    // whose suppression window has passed, so it is taken over instead of being skipped forever - without
    // this, a crash between claim and completion makes every later redelivery a silent no-op and the work
    // is lost. The takeover is a single predicated UPDATE so concurrent redeliveries cannot all win it.
    private async Task<InboxBeginResult> ResolveExistingAsync(
        RelationalConnectionLease<SqlConnection, SqlTransaction> lease, Guid messageId, string handlerTypeName,
        InboxMessageStatus existingStatus, CancellationToken cancellationToken)
    {
        if (existingStatus == InboxMessageStatus.Completed) return InboxBeginResult.AlreadyCompleted;

        using var takeOver = CreateCommand(lease.Connection, $"""
            UPDATE {InboxTable}
            SET Status = @processing, FailureInfoJson = NULL, UpdatedAtUtc = SYSUTCDATETIME()
            WHERE MessageId = @messageId AND HandlerType = @handlerType
              AND Status IN (@processing, @failed)
              AND UpdatedAtUtc <= @staleBefore;
            """, lease.Transaction);
        takeOver.Parameters.AddWithValue("@messageId", messageId);
        takeOver.Parameters.AddWithValue("@handlerType", handlerTypeName);
        takeOver.Parameters.AddWithValue("@processing", (int)InboxMessageStatus.Processing);
        takeOver.Parameters.AddWithValue("@failed", (int)InboxMessageStatus.Failed);
        takeOver.Parameters.AddWithValue("@staleBefore", DateTime.UtcNow.Subtract(options.ClaimRecoveryTimeout));

        if (await takeOver.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0)
            return InboxBeginResult.Started;

        return existingStatus == InboxMessageStatus.Failed
            ? InboxBeginResult.AlreadyFailed
            : InboxBeginResult.AlreadyProcessing;
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

        await using var lease = await RelationalConnectionLease<SqlConnection, SqlTransaction>.OpenAsync(
            sessionAccessor, CreateConnection, cancellationToken).ConfigureAwait(false);
        using var command = CreateCommand(lease.Connection, $"""
            UPDATE {InboxTable}
            SET Status = @status, FailureInfoJson = @failureInfo, UpdatedAtUtc = SYSUTCDATETIME()
            WHERE MessageId = @messageId AND HandlerType = @handlerType;
            """, lease.Transaction);
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
        await using var lease = await RelationalConnectionLease<SqlConnection, SqlTransaction>.OpenAsync(
            sessionAccessor, CreateConnection, cancellationToken).ConfigureAwait(false);
        return await SelectStatusAsync(lease.Connection, lease.Transaction, messageId,
                handlerType.GetSimplifiedQualifiedName(), cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<InboxMessageStatus> SelectStatusAsync(SqlConnection connection, SqlTransaction? transaction,
        Guid messageId, string handlerTypeName, CancellationToken cancellationToken, bool lockForInsert = false)
    {
        using var command = CreateCommand(connection, $"""
            SELECT Status FROM {InboxTable} {(lockForInsert ? "WITH (UPDLOCK, HOLDLOCK)" : string.Empty)}
            WHERE MessageId = @messageId AND HandlerType = @handlerType;
            """, transaction);
        command.Parameters.AddWithValue("@messageId", messageId);
        command.Parameters.AddWithValue("@handlerType", handlerTypeName);

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result == null ? InboxMessageStatus.None : (InboxMessageStatus)(int)result;
    }
}
