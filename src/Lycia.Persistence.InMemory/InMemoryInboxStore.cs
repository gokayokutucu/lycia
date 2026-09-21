// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using System.Collections.Concurrent;
using Lycia.Common.SagaSteps;
using Lycia.Extensions.Configurations;
using Lycia.Saga.Abstractions.Inbox;

namespace Lycia.Persistence.InMemory;

/// <summary>
/// Deterministic in-memory <see cref="IInboxStore"/> for tests and local development. Not durable —
/// state is lost on process restart.
/// </summary>
public class InMemoryInboxStore(InboxOptions? options = null) : IInboxStore
{
    private readonly InboxOptions _options = options ?? new InboxOptions();
    private readonly ConcurrentDictionary<(Guid MessageId, Type HandlerType), Record> _records = new();
    private readonly object _lock = new();

    /// <inheritdoc />
    public Task<InboxBeginResult> TryBeginAsync(Guid messageId, Type handlerType, CancellationToken cancellationToken = default)
    {
        var key = (messageId, handlerType);
        lock (_lock)
        {
            if (_records.TryGetValue(key, out var record))
            {
                // Completed is never reclaimable: suppressing duplicates of committed work is the point.
                if (record.Status == InboxMessageStatus.Completed) return Task.FromResult(InboxBeginResult.AlreadyCompleted);

                var stale = record.UpdatedAtUtc <= DateTime.UtcNow.Subtract(_options.ClaimRecoveryTimeout);
                if (!stale)
                {
                    return Task.FromResult(record.Status == InboxMessageStatus.Failed
                        ? InboxBeginResult.AlreadyFailed
                        : InboxBeginResult.AlreadyProcessing);
                }

                // A claim abandoned by a dead process, or a failed attempt whose suppression window has
                // passed, is taken over rather than skipped forever.
                _records[key] = Record.NewProcessing();
                return Task.FromResult(InboxBeginResult.Started);
            }

            _records[key] = Record.NewProcessing();
            return Task.FromResult(InboxBeginResult.Started);
        }
    }

    /// <inheritdoc />
    public Task MarkCompletedAsync(Guid messageId, Type handlerType, CancellationToken cancellationToken = default) =>
        SetStatus(messageId, handlerType, InboxMessageStatus.Completed);

    /// <inheritdoc />
    public Task MarkFailedAsync(Guid messageId, Type handlerType, SagaStepFailureInfo? failureInfo, CancellationToken cancellationToken = default) =>
        SetStatus(messageId, handlerType, InboxMessageStatus.Failed);

    /// <inheritdoc />
    public Task<InboxMessageStatus> GetStatusAsync(Guid messageId, Type handlerType, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_records.TryGetValue((messageId, handlerType), out var record)
            ? record.Status
            : InboxMessageStatus.None);
    }

    private Task SetStatus(Guid messageId, Type handlerType, InboxMessageStatus status)
    {
        lock (_lock)
        {
            _records[(messageId, handlerType)] = new Record { Status = status, UpdatedAtUtc = DateTime.UtcNow };
        }

        return Task.CompletedTask;
    }

    private sealed class Record
    {
        // Plain setters, not init: this project multi-targets netstandard2.0, which has no IsExternalInit.
        public InboxMessageStatus Status { get; set; }
        public DateTime UpdatedAtUtc { get; set; }

        public static Record NewProcessing() =>
            new() { Status = InboxMessageStatus.Processing, UpdatedAtUtc = DateTime.UtcNow };
    }
}
