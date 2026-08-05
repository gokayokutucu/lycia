// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using System;
using System.Threading;
using System.Threading.Tasks;
using Lycia.Extensions.Eventing;

namespace Lycia.Tests.Helpers;

/// <summary>
/// Deterministic consumer-readiness synchronization for transport integration tests.
/// Tests must await readiness after starting a consume loop and before publishing,
/// instead of sleeping for an arbitrary interval and hoping the consumer caught up.
/// </summary>
public static class EventBusReadiness
{
    /// <summary>
    /// Awaits <see cref="RabbitMqEventBus.ConsumerReady"/> with a bounded wait so a failed or
    /// stuck consumer registration fails the test immediately instead of consuming the full
    /// test timeout. Faults from consumer registration are propagated.
    /// </summary>
    public static async Task WaitForConsumersAsync(
        RabbitMqEventBus bus,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null)
    {
        if (bus == null) throw new ArgumentNullException(nameof(bus));
        var bounded = Task.Delay(timeout ?? TimeSpan.FromSeconds(30), cancellationToken);
        var completed = await Task.WhenAny(bus.ConsumerReady, bounded).ConfigureAwait(false);
        if (completed != bus.ConsumerReady)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException("RabbitMQ consumers were not registered within the readiness timeout.");
        }
        await bus.ConsumerReady.ConfigureAwait(false);
    }
}
