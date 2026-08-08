// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using System.Collections.Concurrent;
using Lycia.Common.SagaSteps;
using Lycia.Saga.Abstractions.Inbox;

namespace Lycia.Persistence.InMemory;

/// <summary>
/// Deterministic in-memory <see cref="IInboxStore"/> for tests and local development. Not durable —
/// state is lost on process restart.
/// </summary>
public class InMemoryInboxStore : IInboxStore
{
    private readonly ConcurrentDictionary<(Guid MessageId, Type HandlerType), InboxMessageStatus> _records = new();
    private readonly object _lock = new();

    /// <inheritdoc />
    public Task<InboxBeginResult> TryBeginAsync(Guid messageId, Type handlerType, CancellationToken cancellationToken = default)
    {
        var key = (messageId, handlerType);
        lock (_lock)
        {
            if (_records.TryGetValue(key, out var status))
            {
                return Task.FromResult(status switch
                {
                    InboxMessageStatus.Processing => InboxBeginResult.AlreadyProcessing,
                    InboxMessageStatus.Completed => InboxBeginResult.AlreadyCompleted,
                    InboxMessageStatus.Failed => InboxBeginResult.AlreadyFailed,
                    _ => InboxBeginResult.Started
                });
            }

            _records[key] = InboxMessageStatus.Processing;
            return Task.FromResult(InboxBeginResult.Started);
        }
    }

    /// <inheritdoc />
    public Task MarkCompletedAsync(Guid messageId, Type handlerType, CancellationToken cancellationToken = default)
    {
        _records[(messageId, handlerType)] = InboxMessageStatus.Completed;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task MarkFailedAsync(Guid messageId, Type handlerType, SagaStepFailureInfo? failureInfo, CancellationToken cancellationToken = default)
    {
        _records[(messageId, handlerType)] = InboxMessageStatus.Failed;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<InboxMessageStatus> GetStatusAsync(Guid messageId, Type handlerType, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_records.TryGetValue((messageId, handlerType), out var status) ? status : InboxMessageStatus.None);
    }
}
