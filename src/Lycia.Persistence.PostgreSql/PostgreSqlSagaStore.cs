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
using Newtonsoft.Json;
using Npgsql;
using NpgsqlTypes;

namespace Lycia.Persistence.PostgreSql;

/// <summary>
/// PostgreSQL backed implementation of <see cref="ISagaStore"/>, with optimistic concurrency
/// (<see cref="IVersionedSagaStore"/>) on saga data and a lightweight health-check round-trip.
/// </summary>
public class PostgreSqlSagaStore(
    PostgreSqlSagaStoreOptions options,
    IEventBus eventBus,
    ISagaIdGenerator sagaIdGenerator,
    ISagaCompensationCoordinator compensationCoordinator,
    IMessageScheduler? messageScheduler = null,
    IOutgoingMessagePipeline? outgoingMessagePipeline = null,
    ILyciaPersistenceSessionAccessor? sessionAccessor = null)
    : ISagaStore, IVersionedSagaStore, ISagaStoreHealthCheck
{
    private const string UniqueViolationSqlState = "23505";

    private string DataTable => options.QualifiedSagaDataTable;
    private string StepsTable => options.QualifiedSagaStepsTable;

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

        await using var lease = await RelationalConnectionLease<NpgsqlConnection, NpgsqlTransaction>.OpenAsync(
            sessionAccessor, CreateConnection).ConfigureAwait(false);
        using var ownedTransaction = lease.OwnsConnection ? lease.Connection.BeginTransaction() : null;
        var transaction = lease.Transaction ?? ownedTransaction
            ?? throw new InvalidOperationException("A relational saga operation requires a transaction.");

        // lycia_saga_steps.saga_id has a foreign key to lycia_saga_data.saga_id, so a step can only be
        // logged once a (possibly placeholder) saga-data row exists for this saga.
        await EnsureSagaDataPlaceholderAsync(lease.Connection, transaction, sagaId).ConfigureAwait(false);

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
                    await UpdateStepAsync(lease.Connection, transaction, sagaId, stepTypeName, handlerTypeName, newMeta)
                        .ConfigureAwait(false);
                }

                if (ownedTransaction != null) ownedTransaction.Commit();
                break;
            case SagaStepValidationResult.Idempotent:
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

    private async Task EnsureSagaDataPlaceholderAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid sagaId)
    {
        using var command = CreateCommand(connection, $"""
            INSERT INTO {DataTable} (saga_id, application_id, saga_data_type, data_json, version, is_completed)
            VALUES (@sagaId, @applicationId, 'Unknown', @emptyData, 0, false)
            ON CONFLICT (saga_id) DO NOTHING;
            """, transaction);
        command.Parameters.AddWithValue("sagaId", sagaId);
        command.Parameters.AddWithValue("applicationId", (object?)ResolveApplicationId() ?? DBNull.Value);
        AddJsonb(command, "emptyData", "{}");
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private async Task InsertStepAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid sagaId,
        string stepTypeName, string handlerTypeName, SagaStepMetadata meta)
    {
        using var command = CreateCommand(connection, $"""
            INSERT INTO {StepsTable}
                (saga_id, step_type, handler_type, message_id, parent_message_id, status, message_type_name, application_id, message_payload, failure_info_json, recorded_at_utc)
            VALUES
                (@sagaId, @stepType, @handlerType, @messageId, @parentMessageId, @status, @messageTypeName, @applicationId, @payload, @failureInfo, now());
            """, transaction);

        AddStepParameters(command, sagaId, stepTypeName, handlerTypeName, meta);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private async Task UpdateStepAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid sagaId,
        string stepTypeName, string handlerTypeName, SagaStepMetadata meta)
    {
        using var command = CreateCommand(connection, $"""
            UPDATE {StepsTable}
            SET status = @status,
                parent_message_id = @parentMessageId,
                message_type_name = @messageTypeName,
                application_id = @applicationId,
                message_payload = @payload,
                failure_info_json = @failureInfo,
                recorded_at_utc = now()
            WHERE saga_id = @sagaId AND step_type = @stepType AND handler_type = @handlerType AND message_id = @messageId;
            """, transaction);

        AddStepParameters(command, sagaId, stepTypeName, handlerTypeName, meta);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static void AddStepParameters(NpgsqlCommand command, Guid sagaId, string stepTypeName, string handlerTypeName,
        SagaStepMetadata meta)
    {
        command.Parameters.AddWithValue("sagaId", sagaId);
        command.Parameters.AddWithValue("stepType", stepTypeName);
        command.Parameters.AddWithValue("handlerType", handlerTypeName);
        command.Parameters.AddWithValue("messageId", meta.MessageId);
        command.Parameters.AddWithValue("parentMessageId", (object?)meta.ParentMessageId ?? DBNull.Value);
        command.Parameters.AddWithValue("status", (int)meta.Status);
        command.Parameters.AddWithValue("messageTypeName", meta.MessageTypeName);
        command.Parameters.AddWithValue("applicationId", (object?)meta.ApplicationId ?? DBNull.Value);
        AddJsonb(command, "payload", meta.MessagePayload);
        AddNullableJsonb(command, "failureInfo", meta.FailureInfo != null ? JsonHelper.SerializeSafe(meta.FailureInfo) : null);
    }

    private async Task<Dictionary<(string stepType, string handlerType, Guid messageId), SagaStepMetadata>>
        SelectAllStepsAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid sagaId)
    {
        var result = new Dictionary<(string, string, Guid), SagaStepMetadata>();

        using var command = CreateCommand(connection, $"""
            SELECT step_type, handler_type, message_id, parent_message_id, status, message_type_name, application_id, message_payload, failure_info_json
            FROM {StepsTable}
            WHERE saga_id = @sagaId;
            """, transaction);
        command.Parameters.AddWithValue("sagaId", sagaId);

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

    private static SagaStepMetadata MapRowToMetadata(NpgsqlDataReader reader, Guid messageId)
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
        await using var lease = await RelationalConnectionLease<NpgsqlConnection, NpgsqlTransaction>.OpenAsync(
            sessionAccessor, CreateConnection).ConfigureAwait(false);
        using var command = CreateCommand(lease.Connection, $"""
            SELECT status FROM {StepsTable}
            WHERE saga_id = @sagaId AND step_type = @stepType AND handler_type = @handlerType AND message_id = @messageId;
            """, lease.Transaction);
        command.Parameters.AddWithValue("sagaId", sagaId);
        command.Parameters.AddWithValue("stepType", stepType.GetSimplifiedQualifiedName());
        command.Parameters.AddWithValue("handlerType", handlerType.GetSimplifiedQualifiedName());
        command.Parameters.AddWithValue("messageId", messageId);

        var result = await command.ExecuteScalarAsync().ConfigureAwait(false);
        return result == null ? StepStatus.None : (StepStatus)(int)result;
    }

    /// <inheritdoc />
    public async Task<KeyValuePair<(string stepType, string handlerType, string messageId), SagaStepMetadata>?>
        GetSagaHandlerStepAsync(Guid sagaId, Guid messageId)
    {
        await using var lease = await RelationalConnectionLease<NpgsqlConnection, NpgsqlTransaction>.OpenAsync(
            sessionAccessor, CreateConnection).ConfigureAwait(false);
        using var command = CreateCommand(lease.Connection, $"""
            SELECT step_type, handler_type, message_id, parent_message_id, status, message_type_name, application_id, message_payload, failure_info_json
            FROM {StepsTable}
            WHERE saga_id = @sagaId AND message_id = @messageId;
            """, lease.Transaction);
        command.Parameters.AddWithValue("sagaId", sagaId);
        command.Parameters.AddWithValue("messageId", messageId);

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
        await using var lease = await RelationalConnectionLease<NpgsqlConnection, NpgsqlTransaction>.OpenAsync(
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
        LoadSagaStepMessageInternalAsync(sagaId, "step_type", stepType.GetSimplifiedQualifiedName());

    /// <inheritdoc />
    public Task<IMessage?> LoadSagaStepMessageAsync(Guid sagaId, Guid messageId) =>
        LoadSagaStepMessageInternalAsync(sagaId, "message_id", messageId);

    private async Task<IMessage?> LoadSagaStepMessageInternalAsync(Guid sagaId, string filterColumn, object filterValue)
    {
        await using var lease = await RelationalConnectionLease<NpgsqlConnection, NpgsqlTransaction>.OpenAsync(
            sessionAccessor, CreateConnection).ConfigureAwait(false);
        using var command = CreateCommand(lease.Connection, $"""
            SELECT message_type_name, message_payload
            FROM {StepsTable}
            WHERE saga_id = @sagaId AND {filterColumn} = @filterValue;
            """, lease.Transaction);
        command.Parameters.AddWithValue("sagaId", sagaId);
        command.Parameters.AddWithValue("filterValue", filterValue);

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
        // write side effect. Eagerly writing a version-1 row on Load (as this used to do) bypasses
        // Split Store's journal/intent bookkeeping in SplitStoreSagaStore.SaveSagaDataAsync, which only
        // runs for explicit Save calls. That produced a canonical version 1 with no corresponding
        // journal entry - a permanent journal gap (previousVersion=1 instead of 0 on the first real,
        // journaled transition) that made every fresh saga fail /debug/sagas/{sagaId}/verify and
        // rebuild-from-journal with JournalGap. The real version 1 write now happens through the first
        // explicit SaveSagaDataAsync call, which is correctly journaled as the Created transition.
        var emptyData = new TSagaData { SagaId = sagaId };
        return emptyData;
    }

    private async Task<TSagaData?> TryLoadSagaDataAsync<TSagaData>(Guid sagaId) where TSagaData : SagaData
    {
        await using var lease = await RelationalConnectionLease<NpgsqlConnection, NpgsqlTransaction>.OpenAsync(
            sessionAccessor, CreateConnection).ConfigureAwait(false);
        return await TryLoadSagaDataAsync<TSagaData>(lease.Connection, lease.Transaction, sagaId).ConfigureAwait(false);
    }

    private async Task<TSagaData?> TryLoadSagaDataAsync<TSagaData>(NpgsqlConnection connection,
        NpgsqlTransaction? transaction, Guid sagaId)
        where TSagaData : SagaData
    {
        using var command = CreateCommand(connection,
            $"SELECT data_json FROM {DataTable} WHERE saga_id = @sagaId AND saga_data_type != 'Unknown';", transaction);
        command.Parameters.AddWithValue("sagaId", sagaId);

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

        await using var lease = await RelationalConnectionLease<NpgsqlConnection, NpgsqlTransaction>.OpenAsync(
            sessionAccessor, CreateConnection).ConfigureAwait(false);

        var dataJson = JsonHelper.SerializeSafe(data);
        var dataType = data.GetType().GetSimplifiedQualifiedName();

        using var command = CreateCommand(lease.Connection, $"""
            INSERT INTO {DataTable} (saga_id, application_id, saga_data_type, data_json, version, is_completed, completed_at_utc, failed_at_utc, updated_at_utc)
            VALUES (@sagaId, @applicationId, @dataType, @dataJson, 1, @isCompleted, @completedAt, @failedAt, now())
            ON CONFLICT (saga_id) DO UPDATE SET
                data_json = EXCLUDED.data_json,
                saga_data_type = EXCLUDED.saga_data_type,
                version = {DataTable}.version + 1,
                is_completed = EXCLUDED.is_completed,
                completed_at_utc = EXCLUDED.completed_at_utc,
                failed_at_utc = EXCLUDED.failed_at_utc,
                application_id = EXCLUDED.application_id,
                updated_at_utc = now()
            RETURNING version;
            """, lease.Transaction);
        AddSagaDataParameters(command, sagaId, dataJson, dataType, data);
        data.Version = Convert.ToInt64(await command.ExecuteScalarAsync().ConfigureAwait(false));

        // The database assigns the new version, so persist the now-authoritative value in the
        // canonical JSON within the same transaction as the row/version update.
        using var synchronizeJson = CreateCommand(lease.Connection, $"""
            UPDATE {DataTable}
            SET data_json = @dataJson
            WHERE saga_id = @sagaId AND version = @version;
            """, lease.Transaction);
        AddJsonb(synchronizeJson, "dataJson", JsonHelper.SerializeSafe(data));
        synchronizeJson.Parameters.AddWithValue("sagaId", sagaId);
        synchronizeJson.Parameters.AddWithValue("version", data.Version);
        await synchronizeJson.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private void AddSagaDataParameters<TSagaData>(NpgsqlCommand command, Guid sagaId, string dataJson, string dataType,
        TSagaData data) where TSagaData : SagaData
    {
        command.Parameters.AddWithValue("sagaId", sagaId);
        command.Parameters.AddWithValue("applicationId", (object?)ResolveApplicationId() ?? DBNull.Value);
        command.Parameters.AddWithValue("dataType", dataType);
        AddJsonb(command, "dataJson", dataJson);
        command.Parameters.AddWithValue("isCompleted", data.IsCompleted);
        command.Parameters.AddWithValue("completedAt", (object?)data.CompletedAt ?? DBNull.Value);
        command.Parameters.AddWithValue("failedAt", (object?)data.FailedAt ?? DBNull.Value);
    }

    /// <inheritdoc />
    public async Task<long> SaveSagaDataAsync<TSagaData>(Guid sagaId, TSagaData data, long expectedVersion)
        where TSagaData : SagaData
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        data.SagaId = sagaId;

        await using var lease = await RelationalConnectionLease<NpgsqlConnection, NpgsqlTransaction>.OpenAsync(
            sessionAccessor, CreateConnection).ConfigureAwait(false);

        var dataJson = JsonHelper.SerializeSafe(data);
        var dataType = data.GetType().GetSimplifiedQualifiedName();

        if (expectedVersion == 0)
        {
            // A row may already exist at version 0 as a saga-data placeholder auto-created by LogStepAsync
            // to satisfy the saga_steps -> saga_data foreign key. Try updating that placeholder in place
            // before falling back to INSERT, so "first save" still succeeds when steps were logged first.
            using (var updatePlaceholder = CreateCommand(lease.Connection, $"""
                UPDATE {DataTable}
                SET data_json = @dataJson, saga_data_type = @dataType, version = 1, is_completed = @isCompleted,
                    completed_at_utc = @completedAt, failed_at_utc = @failedAt, application_id = @applicationId, updated_at_utc = now()
                WHERE saga_id = @sagaId AND version = 0;
                """, lease.Transaction))
            {
                AddSagaDataParameters(updatePlaceholder, sagaId, dataJson, dataType, data);
                var updatedRows = await updatePlaceholder.ExecuteNonQueryAsync().ConfigureAwait(false);
                if (updatedRows > 0)
                {
                    data.Version = 1;
                    return 1;
                }
            }

            try
            {
                using var insert = CreateCommand(lease.Connection, $"""
                    INSERT INTO {DataTable} (saga_id, application_id, saga_data_type, data_json, version, is_completed, completed_at_utc, failed_at_utc, updated_at_utc)
                    VALUES (@sagaId, @applicationId, @dataType, @dataJson, 1, @isCompleted, @completedAt, @failedAt, now());
                    """, lease.Transaction);
                AddSagaDataParameters(insert, sagaId, dataJson, dataType, data);
                await insert.ExecuteNonQueryAsync().ConfigureAwait(false);
                data.Version = 1;
                return 1;
            }
            catch (PostgresException ex) when (ex.SqlState == UniqueViolationSqlState)
            {
                var actual = await SelectCurrentVersionAsync(lease.Connection, lease.Transaction, sagaId)
                    .ConfigureAwait(false) ?? 0;
                throw new SagaConcurrencyException(sagaId, 0, actual);
            }
        }

        using (var update = CreateCommand(lease.Connection, $"""
            UPDATE {DataTable}
            SET data_json = @dataJson, saga_data_type = @dataType, version = version + 1, is_completed = @isCompleted,
                completed_at_utc = @completedAt, failed_at_utc = @failedAt, application_id = @applicationId, updated_at_utc = now()
            WHERE saga_id = @sagaId AND version = @expectedVersion;
            """, lease.Transaction))
        {
            AddSagaDataParameters(update, sagaId, dataJson, dataType, data);
            update.Parameters.AddWithValue("expectedVersion", expectedVersion);
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
        await using var lease = await RelationalConnectionLease<NpgsqlConnection, NpgsqlTransaction>.OpenAsync(
            sessionAccessor, CreateConnection).ConfigureAwait(false);
        using var command = CreateCommand(lease.Connection,
            $"SELECT data_json, version FROM {DataTable} WHERE saga_id = @sagaId;", lease.Transaction);
        command.Parameters.AddWithValue("sagaId", sagaId);

        using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        if (!await reader.ReadAsync().ConfigureAwait(false)) return (new TSagaData(), 0);

        var data = JsonConvert.DeserializeObject<TSagaData>(reader.GetString(0)) ?? new TSagaData();
        data.SagaId = sagaId;
        var version = reader.GetInt64(1);
        data.Version = version;
        return (data, version);
    }

    private async Task<long?> SelectCurrentVersionAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction,
        Guid sagaId)
    {
        using var command = CreateCommand(connection, $"SELECT version FROM {DataTable} WHERE saga_id = @sagaId;",
            transaction);
        command.Parameters.AddWithValue("sagaId", sagaId);
        var result = await command.ExecuteScalarAsync().ConfigureAwait(false);
        return result == null || result == DBNull.Value ? null : (long)result;
    }

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

    private string ResolveApplicationId() => "PostgreSql";

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
