// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using Lycia.Common.Enums;
using Lycia.Common.SagaSteps;
using Lycia.Extensions;
using Lycia.Helpers;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Contexts;
using Lycia.Saga.Abstractions.Messaging;
using Lycia.Saga.Contexts;
using Lycia.Saga.Exceptions;
using Lycia.Saga.Helpers;
using Lycia.Saga.Abstractions.Scheduling;
using Lycia.Saga.Abstractions.Outbox;
using Newtonsoft.Json;
using System.Collections.Concurrent;

namespace Lycia.Stores;

/// <summary>
/// In-memory implementation of ISagaStore for testing or local development.
/// Not suitable for production environments.
/// </summary>
public class InMemorySagaStore(
    IEventBus eventBus,
    ISagaIdGenerator sagaIdGenerator,
    ISagaCompensationCoordinator compensationCoordinator,
    IMessageScheduler? messageScheduler = null,
    IOutgoingMessagePipeline? outgoingMessagePipeline = null) : ISagaStore, IVersionedSagaStore
{
    // Stores saga data per sagaId
    private readonly ConcurrentDictionary<Guid, object> _sagaData = new();

    // Tracks the explicit optimistic-concurrency version per sagaId, independent of _sagaData's object identity.
    private readonly ConcurrentDictionary<Guid, long> _sagaVersions = new();
    private readonly object _versionLock = new();

    // Stores step logs per sagaId with a composite key "stepTypeName_handlerTypeFullName"
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, SagaStepMetadata>> _stepLogs = new();

    // Compensation propagation edges keyed by (sagaId, childMessageId); a single lock guards the small,
    // infrequently-contended claim/complete/fail state machine (mirrors the version lock's role above).
    private readonly ConcurrentDictionary<(Guid SagaId, Guid ChildMessageId), CompensationPropagationIntent> _propagationIntents = new();
    private readonly object _propagationLock = new();

    /// <inheritdoc />
    public Task LogStepAsync(Guid sagaId, Guid messageId, Guid? parentMessageId, Type stepType, StepStatus status,
        Type handlerType, object? payload, Exception? exception, CancellationToken cancellationToken = default)
    {
        return LogStepAsync(sagaId, messageId, parentMessageId, stepType, status, handlerType, payload,
            new SagaStepFailureInfo("Exception occurred", exception?.GetType().Name, exception?.ToString()), cancellationToken);
    }

    /// <inheritdoc />
    public Task LogStepAsync(Guid sagaId, Guid messageId, Guid? parentMessageId, Type stepType, StepStatus status,
        Type handlerType, object? payload, SagaStepFailureInfo? failureInfo,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stepDict = _stepLogs.GetOrAdd(sagaId, _ => new ConcurrentDictionary<string, SagaStepMetadata>());
        var stepKey = NamingHelper.GetStepNameWithHandler(stepType, handlerType, messageId);

        stepDict.TryGetValue(stepKey, out var existingMeta);

        var messageTypeName = SagaStoreLogicHelper.GetMessageTypeName(stepType);

        var metadata = SagaStepMetadata.Build(
            status: status,
            messageId: messageId,
            parentMessageId: parentMessageId,
            messageTypeName: messageTypeName,
            applicationId: "InMemory",
            payload: payload,
            failureInfo: failureInfo);

        // State transition validation
        var result = SagaStepHelper.ValidateSagaStepTransition(messageId, parentMessageId, status, stepDict.Values,
            stepKey, metadata, existingMeta);

        switch (result.ValidationResult)
        {
            case SagaStepValidationResult.ValidTransition:
                stepDict[stepKey] = metadata;
                break;
            case SagaStepValidationResult.Idempotent:
                // Silently ignore idempotent updates
                break;
            case SagaStepValidationResult.DuplicateWithDifferentPayload:
                throw new SagaStepIdempotencyException(result.Message);
            case SagaStepValidationResult.InvalidTransition:
                throw new SagaStepTransitionException(result.Message);
            case SagaStepValidationResult.CircularChain:
                throw new SagaStepCircularChainException(result.Message);
            default:
                throw new InvalidOperationException("Unexpected validation result: " + result.ValidationResult);
        }

        return Task.CompletedTask;
    }


    /// <summary>
    /// Checks if the step with specified stepType and handlerType is completed.
    /// Uses the composite key for lookup.
    /// </summary>
    public Task<bool> IsStepCompletedAsync(Guid sagaId, Guid messageId, Type stepType, Type handlerType)
    {
        if (!_stepLogs.TryGetValue(sagaId, out var steps)) 
            return Task.FromResult(false);
        
        var stepKey = NamingHelper.GetStepNameWithHandler(stepType, handlerType, messageId);
        return Task.FromResult(
            steps.TryGetValue(stepKey, out var metadata) && metadata.Status == StepStatus.Completed
        );

    }

    /// <summary>
    /// Gets the status of the step with specified stepType and handlerType.
    /// Uses the composite key for lookup.
    /// </summary>
    public Task<StepStatus> GetStepStatusAsync(Guid sagaId, Guid messageId, Type stepType, Type handlerType)
    {
        if (_stepLogs.TryGetValue(sagaId, out var steps))
        {
            var stepKey = NamingHelper.GetStepNameWithHandler(stepType, handlerType, messageId);
            if (steps.TryGetValue(stepKey, out var metadata))
            {
                return Task.FromResult(metadata.Status);
            }
        }

        return Task.FromResult(StepStatus.None);
    }

    /// <inheritdoc />
    public Task<KeyValuePair<(string stepType, string handlerType, string messageId), SagaStepMetadata>?>
        GetSagaHandlerStepAsync(Guid sagaId, Guid messageId)
    {
        if (!_stepLogs.TryGetValue(sagaId, out var steps))
            return Task.FromResult<KeyValuePair<(string, string, string), SagaStepMetadata>?>(null);
        foreach (var kvp in steps)
        {
            try
            {
                var (stepTypeName, handlerTypeName, msgId) = SagaStoreLogicHelper.ParseStepKey(kvp.Key);
                if (msgId == messageId.ToString())
                {
                    return Task.FromResult<KeyValuePair<(string, string, string), SagaStepMetadata>?>(
                        new KeyValuePair<(string, string, string), SagaStepMetadata>(
                            (stepTypeName, handlerTypeName, msgId), kvp.Value));
                }
            }
            catch
            {
                // Ignore malformed keys
            }
        }

        return Task.FromResult<KeyValuePair<(string, string, string), SagaStepMetadata>?>(null);
    }

    /// <summary>
    /// Retrieves all saga handler steps for the given sagaId.
    /// Returns a dictionary keyed by (stepType, handlerType) tuple.
    /// </summary>
    public Task<IReadOnlyDictionary<(string stepType, string handlerType, string messageId), SagaStepMetadata>>
        GetSagaHandlerStepsAsync(Guid sagaId)
    {
        if (!_stepLogs.TryGetValue(sagaId, out var steps))
            return Task
                .FromResult<IReadOnlyDictionary<(string stepType, string handlerType, string messageId),
                    SagaStepMetadata>>(
                    new Dictionary<(string stepType, string handlerType, string messageId), SagaStepMetadata>());
        // Parse keys of the form "step:{stepType}:handler:{handlerType}:message:{messageId}"
        var result = new Dictionary<(string stepType, string handlerType, string messageId), SagaStepMetadata>();
        foreach (var kvp in steps)
        {
            var key = kvp.Key;
            var metadata = kvp.Value;

            try
            {
                var (stepTypeName, handlerTypeName, messageId) = SagaStoreLogicHelper.ParseStepKey(key);
                result[(stepTypeName, handlerTypeName, messageId)] = metadata;
            }
            catch
            {
                // ignore malformed keys
            }
        }

        return Task
            .FromResult<IReadOnlyDictionary<(string stepType, string handlerType, string messageId),
                SagaStepMetadata>>(result);

    }
    
    /// <inheritdoc />
    public Task<IMessage?> LoadSagaStepMessageAsync(Guid sagaId, Type stepType)
    {
        if (!_stepLogs.TryGetValue(sagaId, out var steps)) return Task.FromResult<IMessage?>(null);

        foreach (var kvp in steps)
        {
            try
            {
                var (stepTypeName, _, _) = SagaStoreLogicHelper.ParseStepKey(kvp.Key);
                if (stepTypeName != stepType.GetSimplifiedQualifiedName()) continue;

                var meta = kvp.Value;
                var payloadType = Type.GetType(meta.MessageTypeName);
                if (payloadType == null) continue;

                if (JsonConvert.DeserializeObject(meta.MessagePayload, payloadType) is IMessage messageObject)
                    return Task.FromResult<IMessage?>(messageObject);
            }
            catch
            {
                // Ignore malformed keys
            }
        }

        return Task.FromResult<IMessage?>(null);
    }

    /// <inheritdoc />
    public Task<IMessage?> LoadSagaStepMessageAsync(Guid sagaId, Guid messageId)
    {
        if (!_stepLogs.TryGetValue(sagaId, out var steps)) return Task.FromResult<IMessage?>(null);

        foreach (var kvp in steps)
        {
            try
            {
                var (_, _, msgId) = SagaStoreLogicHelper.ParseStepKey(kvp.Key);
                if (msgId != messageId.ToString()) continue;

                var meta = kvp.Value;
                var payloadType = Type.GetType(meta.MessageTypeName);
                if (payloadType == null) continue;

                return Task.FromResult(JsonConvert.DeserializeObject(meta.MessagePayload, payloadType) as IMessage);
            }
            catch
            {
                // Ignore malformed keys
            }
        }

        return Task.FromResult<IMessage?>(null);
    }

    /// <inheritdoc />
    public Task<TSagaData> LoadSagaDataAsync<TSagaData>(Guid sagaId)
        where TSagaData : SagaData, new()
    {
        if (_sagaData.TryGetValue(sagaId, out var data))
        {
            return Task.FromResult((TSagaData)data);
        }
        var defaultData = new TSagaData();
        _sagaData[sagaId] = defaultData;
        return Task.FromResult(defaultData);
    }

    /// <inheritdoc />
    public Task SaveSagaDataAsync<TSagaData>(Guid sagaId, TSagaData? data, CancellationToken cancellationToken = default)
        where TSagaData : SagaData
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (data is null) return Task.CompletedTask;
        data.SagaId = sagaId;

        _sagaData[sagaId] = data;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<ISagaContext<TMessage, TSagaData>> LoadContextAsync<TMessage, TSagaData>(Guid sagaId, TMessage message,
        Type handlerType)
        where TMessage : IMessage
        where TSagaData : SagaData
    {
        if (!_sagaData.TryGetValue(sagaId, out var data))
        {
            _sagaData[sagaId] = data ?? throw new InvalidOperationException(
                $"SagaData instance could not be loaded or created. " +
                $"Please ensure a non-null state is available for saga: {sagaId}");
        }
        
        ISagaContext<TMessage, TSagaData> context = new SagaContext<TMessage, TSagaData>(
            sagaId: sagaId,
            currentStep: message,
            handlerTypeOfCurrentStep: handlerType,
            data: (TSagaData)data,
            eventBus: eventBus, // Use the injected field
            sagaStore: this,
            sagaIdGenerator: sagaIdGenerator, // Use the injected field
            compensationCoordinator: compensationCoordinator,
            messageScheduler: messageScheduler,
            outgoingMessagePipeline: outgoingMessagePipeline
        );

        return Task.FromResult(context);
    }

    /// <inheritdoc />
    public Task<long> SaveSagaDataAsync<TSagaData>(Guid sagaId, TSagaData data, long expectedVersion,
        CancellationToken cancellationToken = default)
        where TSagaData : SagaData
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (data is null) throw new ArgumentNullException(nameof(data));

        lock (_versionLock)
        {
            if (!_sagaVersions.TryGetValue(sagaId, out var currentVersion)) currentVersion = 0L;
            if (currentVersion != expectedVersion)
                throw new SagaConcurrencyException(sagaId, expectedVersion, currentVersion);

            var newVersion = currentVersion + 1;
            data.SagaId = sagaId;
            data.Version = newVersion;
            _sagaData[sagaId] = data;
            _sagaVersions[sagaId] = newVersion;
            return Task.FromResult(newVersion);
        }
    }

    /// <inheritdoc />
    public Task<(TSagaData Data, long Version)> LoadSagaDataWithVersionAsync<TSagaData>(Guid sagaId,
        CancellationToken cancellationToken = default)
        where TSagaData : SagaData, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_versionLock)
        {
            if (!_sagaVersions.TryGetValue(sagaId, out var version)) version = 0L;
            if (_sagaData.TryGetValue(sagaId, out var data))
                return Task.FromResult(((TSagaData)data, version));

            return Task.FromResult((new TSagaData(), 0L));
        }
    }

    /// <inheritdoc />
    public Task<CompensationPropagationClaim> EnsureAndClaimCompensationPropagationAsync(Guid sagaId,
        Guid childMessageId, Guid parentMessageId, string owner, TimeSpan leaseDuration, int maxAttempts,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = (sagaId, childMessageId);
        lock (_propagationLock)
        {
            var now = DateTime.UtcNow;
            var intent = _propagationIntents.GetOrAdd(key, _ => new CompensationPropagationIntent
            {
                SagaId = sagaId,
                ChildMessageId = childMessageId,
                ParentMessageId = parentMessageId,
                Status = CompensationPropagationStatus.Pending,
                AttemptCount = 0,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });

            return Task.FromResult(TryClaimLocked(intent, owner, leaseDuration, maxAttempts, now));
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// <paramref name="leaseDuration"/> is accepted for interface conformance but not used here, matching
    /// the relational providers: the batch path's only staleness threshold is <paramref name="recoveryTimeout"/>,
    /// both for which rows are eligible and for the claim itself - see <see cref="TryClaimLocked"/>.
    /// </remarks>
    public Task<IReadOnlyList<CompensationPropagationIntent>> ClaimDueCompensationPropagationsAsync(int maxCount,
        string owner, TimeSpan leaseDuration, TimeSpan recoveryTimeout, int maxAttempts,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_propagationLock)
        {
            var now = DateTime.UtcNow;
            var claimed = new List<CompensationPropagationIntent>();
            foreach (var intent in _propagationIntents.Values.OrderBy(i => i.CreatedAtUtc))
            {
                if (claimed.Count >= maxCount) break;

                var claimable = intent.Status == CompensationPropagationStatus.Pending
                    || (intent.Status == CompensationPropagationStatus.Claimed
                        && now - intent.UpdatedAtUtc >= recoveryTimeout);
                if (!claimable) continue;

                var result = TryClaimLocked(intent, owner, recoveryTimeout, maxAttempts, now);
                if (result.Outcome == CompensationPropagationClaimOutcome.Claimed) claimed.Add(intent);
            }

            return Task.FromResult<IReadOnlyList<CompensationPropagationIntent>>(claimed);
        }
    }

    // Must be called with _propagationLock held. The sole mutual-exclusion point: a claim only succeeds
    // from Pending, or from Claimed once its lease is considered stale per <paramref name="staleAfter"/>.
    // Callers pass different values for that threshold on purpose: the single-edge ensure-and-claim path
    // passes its own leaseDuration (it intentionally does NOT treat its own freshly-created or already-live
    // Claimed row as claimable, so a concurrent second caller for the same edge correctly receives
    // ClaimedByAnother); the batch claim path passes its recoveryTimeout, matching the exact staleness
    // threshold it already used to decide this intent was even worth attempting - using leaseDuration there
    // instead would wrongly re-reject an intent the caller just determined was due.
    private CompensationPropagationClaim TryClaimLocked(CompensationPropagationIntent intent, string owner,
        TimeSpan staleAfter, int maxAttempts, DateTime now)
    {
        if (intent.Status == CompensationPropagationStatus.Completed)
            return new CompensationPropagationClaim(CompensationPropagationClaimOutcome.AlreadyCompleted, Clone(intent));

        if (intent.Status == CompensationPropagationStatus.Failed)
            return new CompensationPropagationClaim(CompensationPropagationClaimOutcome.AttemptsExhausted, Clone(intent));

        if (intent.Status == CompensationPropagationStatus.Claimed && now - intent.UpdatedAtUtc < staleAfter)
            return new CompensationPropagationClaim(CompensationPropagationClaimOutcome.ClaimedByAnother, Clone(intent));

        if (intent.AttemptCount >= maxAttempts)
        {
            intent.Status = CompensationPropagationStatus.Failed;
            intent.UpdatedAtUtc = now;
            intent.FailureInfo ??= new SagaStepFailureInfo("Compensation propagation attempts exhausted", null, null);
            return new CompensationPropagationClaim(CompensationPropagationClaimOutcome.AttemptsExhausted, Clone(intent));
        }

        intent.Status = CompensationPropagationStatus.Claimed;
        intent.Owner = owner;
        intent.AttemptCount++;
        intent.UpdatedAtUtc = now;
        return new CompensationPropagationClaim(CompensationPropagationClaimOutcome.Claimed, Clone(intent));
    }

    /// <inheritdoc />
    public Task MarkCompensationPropagationCompletedAsync(Guid sagaId, Guid childMessageId,
        CancellationToken cancellationToken = default)
    {
        lock (_propagationLock)
        {
            if (_propagationIntents.TryGetValue((sagaId, childMessageId), out var intent))
            {
                intent.Status = CompensationPropagationStatus.Completed;
                intent.UpdatedAtUtc = DateTime.UtcNow;
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task MarkCompensationPropagationFailedAsync(Guid sagaId, Guid childMessageId,
        SagaStepFailureInfo? failureInfo, CancellationToken cancellationToken = default)
    {
        lock (_propagationLock)
        {
            if (_propagationIntents.TryGetValue((sagaId, childMessageId), out var intent))
            {
                intent.Status = CompensationPropagationStatus.Failed;
                intent.FailureInfo = failureInfo;
                intent.UpdatedAtUtc = DateTime.UtcNow;
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<CompensationPropagationIntent?> GetCompensationPropagationIntentAsync(Guid sagaId, Guid childMessageId,
        CancellationToken cancellationToken = default)
    {
        lock (_propagationLock)
        {
            return Task.FromResult(_propagationIntents.TryGetValue((sagaId, childMessageId), out var intent)
                ? Clone(intent)
                : null);
        }
    }

    private static CompensationPropagationIntent Clone(CompensationPropagationIntent intent) => new()
    {
        SagaId = intent.SagaId,
        ChildMessageId = intent.ChildMessageId,
        ParentMessageId = intent.ParentMessageId,
        Status = intent.Status,
        AttemptCount = intent.AttemptCount,
        Owner = intent.Owner,
        CreatedAtUtc = intent.CreatedAtUtc,
        UpdatedAtUtc = intent.UpdatedAtUtc,
        FailureInfo = intent.FailureInfo
    };
}
