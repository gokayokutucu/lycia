// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
using System.Reflection;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Persistence.Journal;
using Lycia.Saga.Abstractions.Persistence.Reconciliation;

namespace Lycia.Extensions.Journal;

/// <summary>
/// The single rebuild/verify engine used by both automatic recovery and manual/operator-triggered
/// rebuild. Reads canonical journal history, folds it through <see cref="ISagaJournalReducer"/>, and
/// installs the result through the same <see cref="IOperationalSagaProjectionStore"/> CAS/version
/// protection normal Split Store reconciliation uses — so a stale rebuild can never overwrite a newer
/// live projection. Never executes handlers, never publishes, never writes Inbox/Outbox.
/// </summary>
public sealed class SagaRebuildService(
    ISagaJournalStore journalStore,
    ISagaJournalReducer reducer,
    IOperationalSagaProjectionStore operationalStore,
    ISagaStore canonicalStore,
    JournalEntryUpcastChain upcastChain) : ISagaRebuildService
{
    /// <inheritdoc />
    public async Task<SagaRebuildOutcome> RebuildSagaAsync(Guid sagaId, CancellationToken cancellationToken = default)
    {
        var (state, failureKind, reason) = await ReplayAsync(sagaId, 200, cancellationToken).ConfigureAwait(false);
        if (state == null)
            return SagaRebuildOutcome.Failure(sagaId, failureKind, reason ?? "Unknown journal replay failure.");

        await operationalStore.ApplyAsync(new SagaProjectionIntent
        {
            TransitionId = state.LastTransitionId,
            SagaId = state.SagaId,
            ExpectedVersion = 0, // ApplyAsync is idempotent/version-fenced on TargetVersion, not a CAS precondition here.
            TargetVersion = state.Version,
            SagaDataType = state.SagaDataTypeName,
            Payload = state.SagaDataPayload,
            Status = ReconciliationStatus.Applied,
            CreatedAtUtc = DateTime.UtcNow
        }, cancellationToken).ConfigureAwait(false);

        return SagaRebuildOutcome.Success(sagaId, state.Version);
    }

    /// <inheritdoc />
    public async Task<SagaBulkOperationSummary> RebuildAllAsync(SagaRebuildBatchOptions? options = null,
        IProgress<SagaRebuildProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        return await RunBulkAsync(options, progress, RebuildSagaAsync,
            outcome => outcome.Succeeded, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<SagaVerificationOutcome> VerifySagaAsync(Guid sagaId, CancellationToken cancellationToken = default)
    {
        var (state, failureKind, reason) = await ReplayAsync(sagaId, 200, cancellationToken).ConfigureAwait(false);
        if (state == null)
        {
            return new SagaVerificationOutcome
            {
                SagaId = sagaId,
                Status = failureKind switch
                {
                    SagaJournalFailureKind.JournalGap => SagaProjectionVerificationStatus.JournalGap,
                    SagaJournalFailureKind.SchemaUnsupported => SagaProjectionVerificationStatus.SchemaUnsupported,
                    _ => SagaProjectionVerificationStatus.CorruptEntry
                },
                Detail = reason
            };
        }

        var operationalVersion = await operationalStore.GetVersionAsync(sagaId, cancellationToken).ConfigureAwait(false);
        var canonicalVersion = await TryGetCanonicalVersionAsync(state, cancellationToken).ConfigureAwait(false);

        var status = operationalVersion == 0
            ? SagaProjectionVerificationStatus.MissingProjection
            : operationalVersion != state.Version
                ? SagaProjectionVerificationStatus.VersionMismatch
                : canonicalVersion.HasValue && canonicalVersion.Value != state.Version
                    ? SagaProjectionVerificationStatus.StateMismatch
                    : SagaProjectionVerificationStatus.Healthy;

        return new SagaVerificationOutcome
        {
            SagaId = sagaId,
            Status = status,
            JournalVersion = state.Version,
            OperationalProjectionVersion = operationalVersion,
            CanonicalVersion = canonicalVersion
        };
    }

    /// <inheritdoc />
    public async Task<SagaBulkOperationSummary> VerifyAllAsync(SagaRebuildBatchOptions? options = null,
        IProgress<SagaRebuildProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        return await RunBulkAsync(options, progress, VerifySagaAsync,
            outcome => outcome.Status == SagaProjectionVerificationStatus.Healthy, cancellationToken).ConfigureAwait(false);
    }

    private async Task<SagaBulkOperationSummary> RunBulkAsync<TOutcome>(
        SagaRebuildBatchOptions? options,
        IProgress<SagaRebuildProgress>? progress,
        Func<Guid, CancellationToken, Task<TOutcome>> processOne,
        Func<TOutcome, bool> isSuccess,
        CancellationToken cancellationToken)
    {
        options ??= new SagaRebuildBatchOptions();
        var processed = 0;
        var succeeded = 0;
        var failed = 0;
        var failedIds = new List<Guid>();
        Guid? cursor = options.ResumeAfterSagaId;
        Guid? lastSagaId = null;
        var cancelled = false;

        while (true)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }

            var page = await journalStore.EnumerateSagaIdsAsync(cursor, options.PageSize, cancellationToken)
                .ConfigureAwait(false);
            if (page.Count == 0) break;

            foreach (var sagaId in page)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    cancelled = true;
                    break;
                }

                processed++;
                lastSagaId = sagaId;
                bool ok;
                try
                {
                    var outcome = await processOne(sagaId, cancellationToken).ConfigureAwait(false);
                    ok = isSuccess(outcome);
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                    break;
                }
                catch
                {
                    ok = false;
                }

                if (ok) succeeded++;
                else { failed++; failedIds.Add(sagaId); }

                progress?.Report(new SagaRebuildProgress
                {
                    Processed = processed,
                    Succeeded = succeeded,
                    Failed = failed,
                    LastSagaId = sagaId
                });
            }

            if (cancelled) break;
            cursor = page[page.Count - 1];
        }

        return new SagaBulkOperationSummary
        {
            Processed = processed,
            Succeeded = succeeded,
            Failed = failed,
            FailedSagaIds = failedIds,
            ResumeCursor = lastSagaId,
            Cancelled = cancelled
        };
    }

    /// <summary>
    /// Reads one saga's journal in bounded pages and folds it deterministically, validating continuity
    /// as it goes. Only the running <see cref="SagaJournalState"/> is retained — entries are discarded
    /// once folded, so this never loads a saga's entire history into memory at once.
    /// </summary>
    private async Task<(SagaJournalState? State, SagaJournalFailureKind FailureKind, string? Reason)> ReplayAsync(
        Guid sagaId, int readBatchSize, CancellationToken cancellationToken)
    {
        SagaJournalState? state = null;
        long afterVersion = 0;

        while (true)
        {
            var page = await journalStore.ReadAsync(sagaId, afterVersion, readBatchSize, cancellationToken)
                .ConfigureAwait(false);
            if (page.Count == 0) break;

            foreach (var rawEntry in page)
            {
                if (rawEntry.SagaId != sagaId)
                    return (null, SagaJournalFailureKind.CorruptEntry,
                        $"Journal entry {rawEntry.JournalEntryId} belongs to saga {rawEntry.SagaId}, not {sagaId}.");

                SagaJournalEntry entry;
                if (rawEntry.JournalSchemaVersion < SagaJournalSchema.CurrentVersion)
                {
                    var upcast = upcastChain.Upcast(rawEntry);
                    if (!upcast.Succeeded || upcast.Entry == null)
                        return (null, SagaJournalFailureKind.SchemaUnsupported, upcast.FailureReason);
                    entry = upcast.Entry;
                }
                else if (rawEntry.JournalSchemaVersion > SagaJournalSchema.CurrentVersion)
                {
                    return (null, SagaJournalFailureKind.SchemaUnsupported,
                        $"Journal entry {rawEntry.JournalEntryId} declares schema version {rawEntry.JournalSchemaVersion}, " +
                        $"newer than this reducer's supported version {SagaJournalSchema.CurrentVersion}.");
                }
                else
                {
                    entry = rawEntry;
                }

                var expectedPrevious = state?.Version ?? 0;
                if (entry.PreviousVersion != expectedPrevious || entry.TargetVersion <= entry.PreviousVersion)
                {
                    return (null, SagaJournalFailureKind.JournalGap,
                        $"Saga {sagaId}: expected a transition from version {expectedPrevious}, " +
                        $"found one from {entry.PreviousVersion} to {entry.TargetVersion}.");
                }

                state = reducer.Reduce(state, entry);
                afterVersion = entry.TargetVersion;
            }

            if (page.Count < readBatchSize) break;
        }

        return (state, SagaJournalFailureKind.None, null);
    }

    private async Task<long?> TryGetCanonicalVersionAsync(SagaJournalState state, CancellationToken cancellationToken)
    {
        if (canonicalStore is not IVersionedSagaStore versioned) return null;
        if (string.IsNullOrWhiteSpace(state.SagaDataTypeName)) return null;

        try
        {
            var dataType = Type.GetType(state.SagaDataTypeName);
            if (dataType == null) return null;

            var method = typeof(IVersionedSagaStore).GetMethod(nameof(IVersionedSagaStore.LoadSagaDataWithVersionAsync))
                ?? throw new MissingMethodException(nameof(IVersionedSagaStore), nameof(IVersionedSagaStore.LoadSagaDataWithVersionAsync));
            var generic = method.MakeGenericMethod(dataType);
            var task = (Task)generic.Invoke(versioned, [state.SagaId])!;
            await task.ConfigureAwait(false);
            var resultProperty = task.GetType().GetProperty("Result")
                ?? throw new MissingMemberException(task.GetType().Name, "Result");
            var tuple = resultProperty.GetValue(task)!;
            var versionField = tuple.GetType().GetProperty("Version")
                ?? throw new MissingMemberException(tuple.GetType().Name, "Version");
            return (long)versionField.GetValue(tuple)!;
        }
        catch
        {
            // Best-effort secondary check: if the saga-data CLR type cannot be resolved or invoked
            // dynamically, skip the canonical-state comparison rather than failing verification.
            return null;
        }
    }
}
