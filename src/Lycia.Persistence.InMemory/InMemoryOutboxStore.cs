// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using System.Collections.Concurrent;
using Lycia.Common.SagaSteps;
using Lycia.Saga.Abstractions.Outbox;

namespace Lycia.Persistence.InMemory;

/// <summary>
/// Deterministic in-memory <see cref="IOutboxStore"/> for tests and local development. Not durable —
/// state is lost on process restart. Does not publish anything itself; it only tracks capture and
/// lifecycle status for a future publisher worker.
/// </summary>
public class InMemoryOutboxStore : IOutboxStore
{
    private readonly ConcurrentDictionary<Guid, OutboxMessage> _messages = new();
    private readonly object _claimLock = new();

    /// <inheritdoc />
    public Task AddAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        if (message is null) throw new ArgumentNullException(nameof(message));
        _messages.TryAdd(message.MessageId, message);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<OutboxMessage?> GetByMessageIdAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_messages.TryGetValue(messageId, out var message) ? message : null);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<OutboxMessage>> ClaimPendingBatchAsync(int maxCount, CancellationToken cancellationToken = default)
    {
        lock (_claimLock)
        {
            var claimed = _messages.Values
                .Where(m => m.Status == OutboxMessageStatus.Pending)
                .OrderBy(m => m.CreatedAtUtc)
                .Take(maxCount)
                .ToList();

            foreach (var message in claimed)
            {
                message.Status = OutboxMessageStatus.Claimed;
                message.UpdatedAtUtc = DateTime.UtcNow;
            }

            return Task.FromResult<IReadOnlyList<OutboxMessage>>(claimed);
        }
    }

    /// <inheritdoc />
    public Task MarkPublishingAsync(Guid messageId, CancellationToken cancellationToken = default) =>
        SetStatus(messageId, OutboxMessageStatus.Publishing);

    /// <inheritdoc />
    public Task MarkPublishedAsync(Guid messageId, CancellationToken cancellationToken = default) =>
        SetStatus(messageId, OutboxMessageStatus.Published);

    /// <inheritdoc />
    public Task MarkConfirmationUnknownAsync(Guid messageId, CancellationToken cancellationToken = default) =>
        SetStatus(messageId, OutboxMessageStatus.ConfirmationUnknown);

    /// <inheritdoc />
    public Task MarkFailedAsync(Guid messageId, SagaStepFailureInfo? failureInfo, CancellationToken cancellationToken = default)
    {
        if (_messages.TryGetValue(messageId, out var message))
        {
            message.Status = OutboxMessageStatus.Failed;
            message.FailureInfo = failureInfo;
            message.UpdatedAtUtc = DateTime.UtcNow;
        }

        return Task.CompletedTask;
    }

    private Task SetStatus(Guid messageId, OutboxMessageStatus status)
    {
        if (_messages.TryGetValue(messageId, out var message))
        {
            message.Status = status;
            message.UpdatedAtUtc = DateTime.UtcNow;
        }

        return Task.CompletedTask;
    }
}
