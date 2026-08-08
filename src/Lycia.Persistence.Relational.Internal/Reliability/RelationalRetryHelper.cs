// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
namespace Lycia.Persistence.Relational.Internal.Reliability;

/// <summary>
/// Small, dialect-neutral retry helper for transient connection failures (dropped connections, deadlock
/// victims, timeouts). Classification of what counts as "transient" is driver-specific, so callers supply
/// it via <paramref name="isTransient"/> rather than this helper hard-coding any provider's exception types.
/// Never retries business-level failures such as <c>SagaConcurrencyException</c> or saga step-transition
/// exceptions - those must always propagate immediately to the caller.
/// </summary>
public static class RelationalRetryHelper
{
    public static async Task<T> ExecuteAsync<T>(
        Func<Task<T>> action,
        Func<Exception, bool> isTransient,
        int maxAttempts = 3,
        TimeSpan? initialDelay = null,
        CancellationToken cancellationToken = default)
    {
        if (action == null) throw new ArgumentNullException(nameof(action));
        if (isTransient == null) throw new ArgumentNullException(nameof(isTransient));
        if (maxAttempts < 1) throw new ArgumentOutOfRangeException(nameof(maxAttempts));

        var delay = initialDelay ?? TimeSpan.FromMilliseconds(50);
        var attempt = 0;

        while (true)
        {
            attempt++;
            try
            {
                return await action().ConfigureAwait(false);
            }
            catch (Exception ex) when (attempt < maxAttempts && isTransient(ex))
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                delay += delay;
            }
        }
    }

    public static Task ExecuteAsync(
        Func<Task> action,
        Func<Exception, bool> isTransient,
        int maxAttempts = 3,
        TimeSpan? initialDelay = null,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(async () =>
        {
            await action().ConfigureAwait(false);
            return true;
        }, isTransient, maxAttempts, initialDelay, cancellationToken);
    }
}
