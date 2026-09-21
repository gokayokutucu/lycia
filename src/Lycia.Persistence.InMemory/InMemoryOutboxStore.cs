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
    public Task<IReadOnlyList<OutboxMessage>> ClaimPendingBatchAsync(int maxCount,
        CancellationToken cancellationToken = default, int maxAttempts = 5, TimeSpan? recoveryTimeout = null)
    {
        lock (_claimLock)
        {
            var staleBefore = DateTime.UtcNow.Subtract(recoveryTimeout ?? TimeSpan.FromMinutes(1));
            var claimed = _messages.Values
                .Where(m => IsClaimable(m, maxAttempts, staleBefore))
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

    // The cap limits how many attempts may be STARTED, so it gates only the statuses from which a new
    // attempt begins (Pending, ConfirmationUnknown). A Claimed/Publishing row that has gone stale is an
    // attempt whose outcome was never recorded; it is always handed back for recovery, whatever its
    // RetryCount, because otherwise a worker dying on the final attempt would strand the message.
    private static bool IsClaimable(OutboxMessage message, int maxAttempts, DateTime staleBefore) =>
        message.Status switch
        {
            OutboxMessageStatus.Pending => message.RetryCount < maxAttempts,
            OutboxMessageStatus.ConfirmationUnknown =>
                message.RetryCount < maxAttempts && message.UpdatedAtUtc <= staleBefore,
            OutboxMessageStatus.Claimed or OutboxMessageStatus.Publishing => message.UpdatedAtUtc <= staleBefore,
            _ => false
        };

    /// <inheritdoc />
    public Task MarkPublishingAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        if (_messages.TryGetValue(messageId, out var message)) message.RetryCount++;
        return SetStatus(messageId, OutboxMessageStatus.Publishing);
    }

    /// <inheritdoc />
    public Task MarkPublishedAsync(Guid messageId, CancellationToken cancellationToken = default) =>
        SetStatus(messageId, OutboxMessageStatus.Published);

    /// <inheritdoc />
    public Task MarkConfirmationUnknownAsync(Guid messageId, CancellationToken cancellationToken = default) =>
        SetStatus(messageId, OutboxMessageStatus.ConfirmationUnknown);

    /// <inheritdoc />
    public Task MarkFailedAsync(Guid messageId, SagaStepFailureInfo? failureInfo, CancellationToken cancellationToken = default) =>
        SetTerminalStatus(messageId, OutboxMessageStatus.Failed, failureInfo);

    /// <inheritdoc />
    public Task MarkAbandonedAsync(Guid messageId, SagaStepFailureInfo? failureInfo, CancellationToken cancellationToken = default) =>
        SetTerminalStatus(messageId, OutboxMessageStatus.Abandoned, failureInfo);

    private Task SetTerminalStatus(Guid messageId, OutboxMessageStatus status, SagaStepFailureInfo? failureInfo)
    {
        if (_messages.TryGetValue(messageId, out var message))
        {
            message.Status = status;
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
