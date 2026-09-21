// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Common.Enums;
using Lycia.Common.Helpers;
using Lycia.Common.SagaSteps;
using Lycia.Extensions;
using Lycia.Helpers;
using Lycia.Persistence.Relational.Internal.Sessions;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Contexts;
using Lycia.Saga.Abstractions.Messaging;
using Lycia.Saga.Abstractions.Scheduling;
using Lycia.Saga.Abstractions.Outbox;
using Lycia.Saga.Abstractions.Persistence;
using Lycia.Saga.Contexts;
using Lycia.Saga.Exceptions;
using Lycia.Saga.Helpers;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json;

namespace Lycia.Persistence.SqlServer;

/// <summary>
/// Microsoft SQL Server backed implementation of <see cref="ISagaStore"/>, with optimistic concurrency
/// (<see cref="IVersionedSagaStore"/>) on saga data and a lightweight health-check round-trip.
/// </summary>
public class SqlServerSagaStore(
    SqlServerSagaStoreOptions options,
    IEventBus eventBus,
    ISagaIdGenerator sagaIdGenerator,
    ISagaCompensationCoordinator compensationCoordinator,
    IMessageScheduler? messageScheduler = null,
    IOutgoingMessagePipeline? outgoingMessagePipeline = null,
    ILyciaPersistenceSessionAccessor? sessionAccessor = null)
    : ISagaStore, IVersionedSagaStore, ISagaStoreHealthCheck
{
    private const int UniqueOrPrimaryKeyViolation1 = 2627;
    private const int UniqueOrPrimaryKeyViolation2 = 2601;

    private string DataTable => options.SagaDataTable;
    private string StepsTable => options.SagaStepsTable;

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
    public Task LogStepAsync(Guid sagaId, Guid messageId, Guid? parentMessageId, Type stepType, StepStatus status,
        Type handlerType, object? payload, Exception? exception)
    {
        return LogStepAsync(sagaId, messageId, parentMessageId, stepType, status, handlerType, payload,
            new SagaStepFailureInfo("Exception occurred", exception?.GetType().Name, exception?.ToString()));
    }

    /// <inheritdoc />
    public async Task LogStepAsync(Guid sagaId, Guid messageId, Guid? parentMessageId, Type stepType, StepStatus status,
        Type handlerType, object? payload, SagaStepFailureInfo? failureInfo)
    {
        var stepTypeName = stepType.GetSimplifiedQualifiedName();
        var handlerTypeName = handlerType.GetSimplifiedQualifiedName();
        var messageTypeName = SagaStoreLogicHelper.GetMessageTypeName(stepType);
        var stepKey = $"step:{stepTypeName}:handler:{handlerTypeName}:message-id:{messageId}";

        await using var lease = await RelationalConnectionLease<SqlConnection, SqlTransaction>.OpenAsync(
            sessionAccessor, CreateConnection).ConfigureAwait(false);
        using var ownedTransaction = lease.OwnsConnection ? lease.Connection.BeginTransaction() : null;
        var transaction = lease.Transaction ?? ownedTransaction
            ?? throw new InvalidOperationException("A relational saga operation requires a transaction.");

        var allSteps = await SelectAllStepsAsync(lease.Connection, transaction, sagaId).ConfigureAwait(false);
        allSteps.TryGetValue((stepTypeName, handlerTypeName, messageId), out var existingMeta);

        var newMeta = SagaStepMetadata.Build(status, messageId, parentMessageId, messageTypeName,
            ResolveApplicationId(), payload, failureInfo);

        var result = SagaStepHelper.ValidateSagaStepTransition(messageId, parentMessageId, status,
            allSteps.Values, stepKey, newMeta, existingMeta);

        switch (result.ValidationResult)
        {
            case SagaStepValidationResult.ValidTransition:
                if (existingMeta == null)
                {
                    await InsertStepAsync(lease.Connection, transaction, sagaId, stepTypeName, handlerTypeName, newMeta)
                        .ConfigureAwait(false);
                }
                else
                {
                    await UpdateStepAsync(lease.Connection, transaction, sagaId, stepTypeName, handlerTypeName, messageId, newMeta)
                        .ConfigureAwait(false);
                }

                if (ownedTransaction != null) ownedTransaction.Commit();
                break;
            case SagaStepValidationResult.Idempotent:
                // Silently ignore idempotent updates.
                if (ownedTransaction != null) ownedTransaction.Rollback();
                break;
            case SagaStepValidationResult.DuplicateWithDifferentPayload:
                if (ownedTransaction != null) ownedTransaction.Rollback();
                throw new SagaStepIdempotencyException(result.Message);
            case SagaStepValidationResult.InvalidTransition:
                if (ownedTransaction != null) ownedTransaction.Rollback();
                throw new SagaStepTransitionException(result.Message);
            case SagaStepValidationResult.CircularChain:
                if (ownedTransaction != null) ownedTransaction.Rollback();
                throw new SagaStepCircularChainException(result.Message);
            default:
                if (ownedTransaction != null) ownedTransaction.Rollback();
                throw new InvalidOperationException("Unexpected validation result: " + result.ValidationResult);
        }
    }

    private async Task InsertStepAsync(SqlConnection connection, SqlTransaction transaction, Guid sagaId,
        string stepTypeName, string handlerTypeName, SagaStepMetadata meta)
    {
        using var command = CreateCommand(connection, $"""
            INSERT INTO {StepsTable}
                (SagaId, StepType, HandlerType, MessageId, ParentMessageId, Status, MessageTypeName, ApplicationId, MessagePayload, FailureInfoJson, RecordedAtUtc)
            VALUES
                (@sagaId, @stepType, @handlerType, @messageId, @parentMessageId, @status, @messageTypeName, @applicationId, @payload, @failureInfo, SYSUTCDATETIME());
            """, transaction);

        AddStepParameters(command, sagaId, stepTypeName, handlerTypeName, meta);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private async Task UpdateStepAsync(SqlConnection connection, SqlTransaction transaction, Guid sagaId,
        string stepTypeName, string handlerTypeName, Guid messageId, SagaStepMetadata meta)
    {
        using var command = CreateCommand(connection, $"""
            UPDATE {StepsTable}
            SET Status = @status,
                ParentMessageId = @parentMessageId,
                MessageTypeName = @messageTypeName,
                ApplicationId = @applicationId,
                MessagePayload = @payload,
                FailureInfoJson = @failureInfo,
                RecordedAtUtc = SYSUTCDATETIME()
            WHERE SagaId = @sagaId AND StepType = @stepType AND HandlerType = @handlerType AND MessageId = @messageId;
            """, transaction);

        AddStepParameters(command, sagaId, stepTypeName, handlerTypeName, meta);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static void AddStepParameters(SqlCommand command, Guid sagaId, string stepTypeName, string handlerTypeName,
        SagaStepMetadata meta)
    {
        command.Parameters.AddWithValue("@sagaId", sagaId);
        command.Parameters.AddWithValue("@stepType", stepTypeName);
        command.Parameters.AddWithValue("@handlerType", handlerTypeName);
        command.Parameters.AddWithValue("@messageId", meta.MessageId);
        command.Parameters.AddWithValue("@parentMessageId", (object?)meta.ParentMessageId ?? DBNull.Value);
        command.Parameters.AddWithValue("@status", (int)meta.Status);
        command.Parameters.AddWithValue("@messageTypeName", meta.MessageTypeName);
        command.Parameters.AddWithValue("@applicationId", (object?)meta.ApplicationId ?? DBNull.Value);
        command.Parameters.AddWithValue("@payload", meta.MessagePayload);
        command.Parameters.AddWithValue("@failureInfo",
            (object?)(meta.FailureInfo != null ? JsonHelper.SerializeSafe(meta.FailureInfo) : null) ?? DBNull.Value);
    }

    private async Task<Dictionary<(string stepType, string handlerType, Guid messageId), SagaStepMetadata>>
        SelectAllStepsAsync(SqlConnection connection, SqlTransaction? transaction, Guid sagaId)
    {
        var result = new Dictionary<(string, string, Guid), SagaStepMetadata>();

        using var command = CreateCommand(connection, $"""
            SELECT StepType, HandlerType, MessageId, ParentMessageId, Status, MessageTypeName, ApplicationId, MessagePayload, FailureInfoJson
            FROM {StepsTable}
            WHERE SagaId = @sagaId;
            """, transaction);
        command.Parameters.AddWithValue("@sagaId", sagaId);

        using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            var stepTypeName = reader.GetString(0);
            var handlerTypeName = reader.GetString(1);
            var messageId = reader.GetGuid(2);
            var meta = MapRowToMetadata(reader, messageId);
            result[(stepTypeName, handlerTypeName, messageId)] = meta;
        }

        return result;
    }

    private static SagaStepMetadata MapRowToMetadata(SqlDataReader reader, Guid messageId)
    {
        var parentMessageId = reader.IsDBNull(3) ? (Guid?)null : reader.GetGuid(3);
        var status = (StepStatus)reader.GetInt32(4);
        var messageTypeName = reader.GetString(5);
        var applicationId = reader.IsDBNull(6) ? null : reader.GetString(6);
        var payload = reader.GetString(7);
        var failureInfoJson = reader.IsDBNull(8) ? null : reader.GetString(8);
        var failureInfo = failureInfoJson == null
            ? null
            : JsonConvert.DeserializeObject<SagaStepFailureInfo>(failureInfoJson);

        return new SagaStepMetadata
        {
            MessageId = messageId,
            ParentMessageId = parentMessageId,
            Status = status,
            MessageTypeName = messageTypeName,
            ApplicationId = applicationId,
            MessagePayload = payload,
            FailureInfo = failureInfo
        };
    }

    /// <inheritdoc />
    public async Task<bool> IsStepCompletedAsync(Guid sagaId, Guid messageId, Type stepType, Type handlerType)
    {
        var status = await GetStepStatusAsync(sagaId, messageId, stepType, handlerType).ConfigureAwait(false);
        return status == StepStatus.Completed;
    }

    /// <inheritdoc />
    public async Task<StepStatus> GetStepStatusAsync(Guid sagaId, Guid messageId, Type stepType, Type handlerType)
    {
        await using var lease = await RelationalConnectionLease<SqlConnection, SqlTransaction>.OpenAsync(
            sessionAccessor, CreateConnection).ConfigureAwait(false);
        using var command = CreateCommand(lease.Connection, $"""
            SELECT Status FROM {StepsTable}
            WHERE SagaId = @sagaId AND StepType = @stepType AND HandlerType = @handlerType AND MessageId = @messageId;
            """, lease.Transaction);
        command.Parameters.AddWithValue("@sagaId", sagaId);
        command.Parameters.AddWithValue("@stepType", stepType.GetSimplifiedQualifiedName());
        command.Parameters.AddWithValue("@handlerType", handlerType.GetSimplifiedQualifiedName());
        command.Parameters.AddWithValue("@messageId", messageId);

        var result = await command.ExecuteScalarAsync().ConfigureAwait(false);
        return result == null ? StepStatus.None : (StepStatus)(int)result;
    }

    /// <inheritdoc />
    public async Task<KeyValuePair<(string stepType, string handlerType, string messageId), SagaStepMetadata>?>
        GetSagaHandlerStepAsync(Guid sagaId, Guid messageId)
    {
        await using var lease = await RelationalConnectionLease<SqlConnection, SqlTransaction>.OpenAsync(
            sessionAccessor, CreateConnection).ConfigureAwait(false);
        using var command = CreateCommand(lease.Connection, $"""
            SELECT StepType, HandlerType, MessageId, ParentMessageId, Status, MessageTypeName, ApplicationId, MessagePayload, FailureInfoJson
            FROM {StepsTable}
            WHERE SagaId = @sagaId AND MessageId = @messageId;
            """, lease.Transaction);
        command.Parameters.AddWithValue("@sagaId", sagaId);
        command.Parameters.AddWithValue("@messageId", messageId);

        using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        if (!await reader.ReadAsync().ConfigureAwait(false)) return null;

        var stepTypeName = reader.GetString(0);
        var handlerTypeName = reader.GetString(1);
        var msgId = reader.GetGuid(2);
        var meta = MapRowToMetadata(reader, msgId);

        return new KeyValuePair<(string, string, string), SagaStepMetadata>(
            (stepTypeName, handlerTypeName, msgId.ToString()), meta);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<(string stepType, string handlerType, string messageId), SagaStepMetadata>>
        GetSagaHandlerStepsAsync(Guid sagaId)
    {
        await using var lease = await RelationalConnectionLease<SqlConnection, SqlTransaction>.OpenAsync(
            sessionAccessor, CreateConnection).ConfigureAwait(false);
        var stepsById = await SelectAllStepsAsync(lease.Connection, lease.Transaction, sagaId).ConfigureAwait(false);
        var result = new Dictionary<(string stepType, string handlerType, string messageId), SagaStepMetadata>();
        foreach (var entry in stepsById)
        {
            result[(entry.Key.stepType, entry.Key.handlerType, entry.Key.messageId.ToString())] = entry.Value;
        }

        return result;
    }

    /// <inheritdoc />
    public Task<IMessage?> LoadSagaStepMessageAsync(Guid sagaId, Type stepType) =>
        LoadSagaStepMessageInternalAsync(sagaId, "StepType", stepType.GetSimplifiedQualifiedName());

    /// <inheritdoc />
    public Task<IMessage?> LoadSagaStepMessageAsync(Guid sagaId, Guid messageId) =>
        LoadSagaStepMessageInternalAsync(sagaId, "MessageId", messageId);

    private async Task<IMessage?> LoadSagaStepMessageInternalAsync(Guid sagaId, string filterColumn, object filterValue)
    {
        await using var lease = await RelationalConnectionLease<SqlConnection, SqlTransaction>.OpenAsync(
            sessionAccessor, CreateConnection).ConfigureAwait(false);
        using var command = CreateCommand(lease.Connection, $"""
            SELECT MessageTypeName, MessagePayload
            FROM {StepsTable}
            WHERE SagaId = @sagaId AND {filterColumn} = @filterValue;
            """, lease.Transaction);
        command.Parameters.AddWithValue("@sagaId", sagaId);
        command.Parameters.AddWithValue("@filterValue", filterValue);

        using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            try
            {
                var messageTypeName = reader.GetString(0);
                var payloadJson = reader.GetString(1);
                var payloadType = Type.GetType(messageTypeName);
                if (payloadType == null) continue;

                if (JsonConvert.DeserializeObject(payloadJson, payloadType) is IMessage messageObject)
                    return messageObject;
            }
            catch
            {
                // Ignore malformed rows, matching InMemory/Redis behavior.
            }
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<TSagaData> LoadSagaDataAsync<TSagaData>(Guid sagaId) where TSagaData : SagaData, new()
    {
        var loaded = await TryLoadSagaDataAsync<TSagaData>(sagaId).ConfigureAwait(false);
        if (loaded != null) return loaded;

        // Do not persist a canonical row here: a Load with no prior data must be a pure read with no
        // write side effect. Eagerly writing a version-1 row on Load bypasses Split Store's
        // journal/intent bookkeeping (which only runs for explicit Save calls), producing a canonical
        // version 1 with no corresponding journal entry - a permanent journal gap. See the identical
        // fix and full explanation in PostgreSqlSagaStore.LoadSagaDataAsync, which reproduced and
        // confirmed this live via /debug/sagas/{sagaId}/verify returning JournalGap for every fresh saga.
        var emptyData = new TSagaData { SagaId = sagaId };
        return emptyData;
    }

    private async Task<TSagaData?> TryLoadSagaDataAsync<TSagaData>(Guid sagaId) where TSagaData : SagaData
    {
        await using var lease = await RelationalConnectionLease<SqlConnection, SqlTransaction>.OpenAsync(
            sessionAccessor, CreateConnection).ConfigureAwait(false);
        return await TryLoadSagaDataAsync<TSagaData>(lease.Connection, lease.Transaction, sagaId).ConfigureAwait(false);
    }

    private async Task<TSagaData?> TryLoadSagaDataAsync<TSagaData>(SqlConnection connection, SqlTransaction? transaction,
        Guid sagaId) where TSagaData : SagaData
    {
        using var command = CreateCommand(connection, $"SELECT DataJson FROM {DataTable} WHERE SagaId = @sagaId;", transaction);
        command.Parameters.AddWithValue("@sagaId", sagaId);

        var result = await command.ExecuteScalarAsync().ConfigureAwait(false);
        if (result == null || result == DBNull.Value) return null;

        var data = JsonConvert.DeserializeObject<TSagaData>((string)result);
        if (data != null) data.SagaId = sagaId;
        return data;
    }

    /// <inheritdoc />
    public async Task SaveSagaDataAsync<TSagaData>(Guid sagaId, TSagaData? data) where TSagaData : SagaData
    {
        if (data is null) return;
        data.SagaId = sagaId;

        await using var lease = await RelationalConnectionLease<SqlConnection, SqlTransaction>.OpenAsync(
            sessionAccessor, CreateConnection).ConfigureAwait(false);

        var dataJson = JsonHelper.SerializeSafe(data);
        var dataType = data.GetType().GetSimplifiedQualifiedName();

        using (var update = CreateCommand(lease.Connection, $"""
            UPDATE {DataTable}
            SET DataJson = @dataJson, SagaDataType = @dataType, Version = Version + 1, IsCompleted = @isCompleted,
                CompletedAtUtc = @completedAt, FailedAtUtc = @failedAt, ApplicationId = @applicationId, UpdatedAtUtc = SYSUTCDATETIME()
            OUTPUT inserted.Version
            WHERE SagaId = @sagaId;
            """, lease.Transaction))
        {
            AddSagaDataParameters(update, sagaId, dataJson, dataType, data);
            var version = await update.ExecuteScalarAsync().ConfigureAwait(false);
            if (version != null)
            {
                data.Version = Convert.ToInt64(version);
                await SynchronizeSerializedVersionAsync(lease.Connection, lease.Transaction, sagaId, data)
                    .ConfigureAwait(false);
                return;
            }
        }

        try
        {
            using var insert = CreateCommand(lease.Connection, $"""
                INSERT INTO {DataTable} (SagaId, ApplicationId, SagaDataType, DataJson, Version, IsCompleted, CompletedAtUtc, FailedAtUtc, UpdatedAtUtc)
                OUTPUT inserted.Version
                VALUES (@sagaId, @applicationId, @dataType, @dataJson, 1, @isCompleted, @completedAt, @failedAt, SYSUTCDATETIME());
                """, lease.Transaction);
            AddSagaDataParameters(insert, sagaId, dataJson, dataType, data);
            data.Version = Convert.ToInt64(await insert.ExecuteScalarAsync().ConfigureAwait(false));
        }
        catch (SqlException ex) when (IsUniqueViolation(ex))
        {
            // Lost the race against a concurrent first-write; the other writer's row now exists, so
            // fall back to an update to still honor last-write-wins semantics for this non-versioned API.
            using var update = CreateCommand(lease.Connection, $"""
                UPDATE {DataTable}
                SET DataJson = @dataJson, SagaDataType = @dataType, Version = Version + 1, IsCompleted = @isCompleted,
                    CompletedAtUtc = @completedAt, FailedAtUtc = @failedAt, ApplicationId = @applicationId, UpdatedAtUtc = SYSUTCDATETIME()
                OUTPUT inserted.Version
                WHERE SagaId = @sagaId;
                """, lease.Transaction);
            AddSagaDataParameters(update, sagaId, dataJson, dataType, data);
            data.Version = Convert.ToInt64(await update.ExecuteScalarAsync().ConfigureAwait(false));
        }

        await SynchronizeSerializedVersionAsync(lease.Connection, lease.Transaction, sagaId, data)
            .ConfigureAwait(false);
    }

    private async Task SynchronizeSerializedVersionAsync<TSagaData>(SqlConnection connection,
        SqlTransaction? transaction, Guid sagaId, TSagaData data) where TSagaData : SagaData
    {
        using var command = CreateCommand(connection, $"""
            UPDATE {DataTable}
            SET DataJson = @dataJson
            WHERE SagaId = @sagaId AND Version = @version;
            """, transaction);
        command.Parameters.AddWithValue("@dataJson", JsonHelper.SerializeSafe(data));
        command.Parameters.AddWithValue("@sagaId", sagaId);
        command.Parameters.AddWithValue("@version", data.Version);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private void AddSagaDataParameters<TSagaData>(SqlCommand command, Guid sagaId, string dataJson, string dataType,
        TSagaData data) where TSagaData : SagaData
    {
        command.Parameters.AddWithValue("@sagaId", sagaId);
        command.Parameters.AddWithValue("@applicationId", (object?)ResolveApplicationId() ?? DBNull.Value);
        command.Parameters.AddWithValue("@dataType", dataType);
        command.Parameters.AddWithValue("@dataJson", dataJson);
        command.Parameters.AddWithValue("@isCompleted", data.IsCompleted);
        command.Parameters.AddWithValue("@completedAt", (object?)data.CompletedAt ?? DBNull.Value);
        command.Parameters.AddWithValue("@failedAt", (object?)data.FailedAt ?? DBNull.Value);
    }

    /// <inheritdoc />
    public async Task<long> SaveSagaDataAsync<TSagaData>(Guid sagaId, TSagaData data, long expectedVersion)
        where TSagaData : SagaData
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        data.SagaId = sagaId;

        await using var lease = await RelationalConnectionLease<SqlConnection, SqlTransaction>.OpenAsync(
            sessionAccessor, CreateConnection).ConfigureAwait(false);

        var dataJson = JsonHelper.SerializeSafe(data);
        var dataType = data.GetType().GetSimplifiedQualifiedName();

        if (expectedVersion == 0)
        {
            try
            {
                using var insert = CreateCommand(lease.Connection, $"""
                    INSERT INTO {DataTable} (SagaId, ApplicationId, SagaDataType, DataJson, Version, IsCompleted, CompletedAtUtc, FailedAtUtc, UpdatedAtUtc)
                    VALUES (@sagaId, @applicationId, @dataType, @dataJson, 1, @isCompleted, @completedAt, @failedAt, SYSUTCDATETIME());
                    """, lease.Transaction);
                AddSagaDataParameters(insert, sagaId, dataJson, dataType, data);
                await insert.ExecuteNonQueryAsync().ConfigureAwait(false);
                data.Version = 1;
                return 1;
            }
            catch (SqlException ex) when (IsUniqueViolation(ex))
            {
                var actual = await SelectCurrentVersionAsync(lease.Connection, lease.Transaction, sagaId)
                    .ConfigureAwait(false) ?? 0;
                throw new SagaConcurrencyException(sagaId, 0, actual);
            }
        }

        using (var update = CreateCommand(lease.Connection, $"""
            UPDATE {DataTable}
            SET DataJson = @dataJson, SagaDataType = @dataType, Version = Version + 1, IsCompleted = @isCompleted,
                CompletedAtUtc = @completedAt, FailedAtUtc = @failedAt, ApplicationId = @applicationId, UpdatedAtUtc = SYSUTCDATETIME()
            WHERE SagaId = @sagaId AND Version = @expectedVersion;
            """, lease.Transaction))
        {
            AddSagaDataParameters(update, sagaId, dataJson, dataType, data);
            update.Parameters.AddWithValue("@expectedVersion", expectedVersion);
            var rows = await update.ExecuteNonQueryAsync().ConfigureAwait(false);
            if (rows > 0)
            {
                var newVersion = expectedVersion + 1;
                data.Version = newVersion;
                return newVersion;
            }
        }

        var actualVersion = await SelectCurrentVersionAsync(lease.Connection, lease.Transaction, sagaId)
            .ConfigureAwait(false) ?? 0;
        throw new SagaConcurrencyException(sagaId, expectedVersion, actualVersion);
    }

    /// <inheritdoc />
    public async Task<(TSagaData Data, long Version)> LoadSagaDataWithVersionAsync<TSagaData>(Guid sagaId)
        where TSagaData : SagaData, new()
    {
        await using var lease = await RelationalConnectionLease<SqlConnection, SqlTransaction>.OpenAsync(
            sessionAccessor, CreateConnection).ConfigureAwait(false);
        using var command = CreateCommand(lease.Connection,
            $"SELECT DataJson, Version FROM {DataTable} WHERE SagaId = @sagaId;", lease.Transaction);
        command.Parameters.AddWithValue("@sagaId", sagaId);

        using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        if (!await reader.ReadAsync().ConfigureAwait(false)) return (new TSagaData(), 0);

        var data = JsonConvert.DeserializeObject<TSagaData>(reader.GetString(0)) ?? new TSagaData();
        data.SagaId = sagaId;
        var version = reader.GetInt64(1);
        data.Version = version;
        return (data, version);
    }

    private async Task<long?> SelectCurrentVersionAsync(SqlConnection connection, SqlTransaction? transaction, Guid sagaId)
    {
        using var command = CreateCommand(connection, $"SELECT Version FROM {DataTable} WHERE SagaId = @sagaId;", transaction);
        command.Parameters.AddWithValue("@sagaId", sagaId);
        var result = await command.ExecuteScalarAsync().ConfigureAwait(false);
        return result == null || result == DBNull.Value ? null : (long)result;
    }

    private static bool IsUniqueViolation(SqlException ex) =>
        ex.Errors.Cast<SqlError>().Any(e => e.Number is UniqueOrPrimaryKeyViolation1 or UniqueOrPrimaryKeyViolation2);

    /// <inheritdoc />
    public async Task<ISagaContext<TMessage, TSagaData>> LoadContextAsync<TMessage, TSagaData>(Guid sagaId, TMessage message,
        Type handlerType)
        where TMessage : IMessage
        where TSagaData : SagaData
    {
        var data = await TryLoadSagaDataAsync<TSagaData>(sagaId).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"SagaData instance could not be loaded or created. " +
                $"Please ensure a non-null state is available for saga: {sagaId}");

        ISagaContext<TMessage, TSagaData> context = new SagaContext<TMessage, TSagaData>(
            sagaId: sagaId,
            currentStep: message,
            handlerTypeOfCurrentStep: handlerType,
            data: data,
            eventBus: eventBus,
            sagaStore: this,
            sagaIdGenerator: sagaIdGenerator,
            compensationCoordinator: compensationCoordinator,
            messageScheduler: messageScheduler,
            outgoingMessagePipeline: outgoingMessagePipeline
        );

        return context;
    }

    private string ResolveApplicationId() => "SqlServer";

    /// <inheritdoc />
    public async Task<bool> PingAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            using var command = CreateCommand(connection, "SELECT 1;");
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
