// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
using System.Security.Cryptography;
using System.Text;
using Lycia.Common.Enums;
using Lycia.Common.SagaSteps;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Contexts;
using Lycia.Saga.Abstractions.Messaging;
using Lycia.Saga.Abstractions.Persistence.Reconciliation;
using Newtonsoft.Json;

namespace Lycia.Extensions.SplitStore;

internal sealed class SplitStoreSagaStore(ISagaStore canonicalStore, IReconciliationStore reconciliationStore)
    : ISagaStore, IVersionedSagaStore
{
    public Task LogStepAsync(Guid sagaId, Guid messageId, Guid? parentMessageId, Type stepType, StepStatus status,
        Type handlerType, object? payload, Exception? exception) =>
        canonicalStore.LogStepAsync(sagaId, messageId, parentMessageId, stepType, status, handlerType, payload, exception);

    public Task LogStepAsync(Guid sagaId, Guid messageId, Guid? parentMessageId, Type stepType, StepStatus status,
        Type handlerType, object? payload, SagaStepFailureInfo? failureInfo) =>
        canonicalStore.LogStepAsync(sagaId, messageId, parentMessageId, stepType, status, handlerType, payload, failureInfo);

    public Task<bool> IsStepCompletedAsync(Guid sagaId, Guid messageId, Type stepType, Type handlerType) =>
        canonicalStore.IsStepCompletedAsync(sagaId, messageId, stepType, handlerType);

    public Task<StepStatus> GetStepStatusAsync(Guid sagaId, Guid messageId, Type stepType, Type handlerType) =>
        canonicalStore.GetStepStatusAsync(sagaId, messageId, stepType, handlerType);

    public Task<KeyValuePair<(string stepType, string handlerType, string messageId), SagaStepMetadata>?>
        GetSagaHandlerStepAsync(Guid sagaId, Guid messageId) =>
        canonicalStore.GetSagaHandlerStepAsync(sagaId, messageId);

    public Task<IReadOnlyDictionary<(string stepType, string handlerType, string messageId), SagaStepMetadata>>
        GetSagaHandlerStepsAsync(Guid sagaId) => canonicalStore.GetSagaHandlerStepsAsync(sagaId);

    public Task<IMessage?> LoadSagaStepMessageAsync(Guid sagaId, Type stepType) =>
        canonicalStore.LoadSagaStepMessageAsync(sagaId, stepType);

    public Task<IMessage?> LoadSagaStepMessageAsync(Guid sagaId, Guid messageId) =>
        canonicalStore.LoadSagaStepMessageAsync(sagaId, messageId);

    public Task<TSagaData> LoadSagaDataAsync<TSagaData>(Guid sagaId) where TSagaData : SagaData, new() =>
        canonicalStore.LoadSagaDataAsync<TSagaData>(sagaId);

    public async Task SaveSagaDataAsync<TSagaData>(Guid sagaId, TSagaData? data) where TSagaData : SagaData
    {
        if (data is null) return;
        await canonicalStore.SaveSagaDataAsync(sagaId, data).ConfigureAwait(false);
        var version = data.Version;
        if (version <= 0)
            throw new InvalidOperationException("The canonical SagaStore did not return an authoritative saga version.");
        await AddIntentAsync(sagaId, data, Math.Max(0, version - 1), version).ConfigureAwait(false);
    }

    public async Task<long> SaveSagaDataAsync<TSagaData>(Guid sagaId, TSagaData data, long expectedVersion)
        where TSagaData : SagaData
    {
        if (canonicalStore is not IVersionedSagaStore versioned)
            throw new InvalidOperationException("Split Store requires a versioned relational canonical SagaStore.");

        var targetVersion = await versioned.SaveSagaDataAsync(sagaId, data, expectedVersion).ConfigureAwait(false);
        await AddIntentAsync(sagaId, data, expectedVersion, targetVersion).ConfigureAwait(false);
        return targetVersion;
    }

    public Task<(TSagaData Data, long Version)> LoadSagaDataWithVersionAsync<TSagaData>(Guid sagaId)
        where TSagaData : SagaData, new()
    {
        if (canonicalStore is not IVersionedSagaStore versioned)
            throw new InvalidOperationException("Split Store requires a versioned relational canonical SagaStore.");
        return versioned.LoadSagaDataWithVersionAsync<TSagaData>(sagaId);
    }

    public Task<ISagaContext<TMessage, TSagaData>> LoadContextAsync<TMessage, TSagaData>(Guid sagaId,
        TMessage message, Type handlerType)
        where TMessage : IMessage
        where TSagaData : SagaData => canonicalStore.LoadContextAsync<TMessage, TSagaData>(sagaId, message, handlerType);

    private Task AddIntentAsync<TSagaData>(Guid sagaId, TSagaData data, long expectedVersion, long targetVersion)
        where TSagaData : SagaData
    {
        data.SagaId = sagaId;
        data.Version = targetVersion;
        return reconciliationStore.AddAsync(new SagaProjectionIntent
        {
            TransitionId = CreateTransitionId(sagaId, targetVersion),
            SagaId = sagaId,
            ExpectedVersion = expectedVersion,
            TargetVersion = targetVersion,
            SagaDataType = data.GetType().AssemblyQualifiedName ?? data.GetType().FullName ?? data.GetType().Name,
            Payload = JsonConvert.SerializeObject(data),
            Status = ReconciliationStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow
        });
    }

    private static Guid CreateTransitionId(Guid sagaId, long targetVersion)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes($"{sagaId:N}:{targetVersion}"));
        var bytes = new byte[16];
        Array.Copy(hash, bytes, bytes.Length);
        return new Guid(bytes);
    }
}
