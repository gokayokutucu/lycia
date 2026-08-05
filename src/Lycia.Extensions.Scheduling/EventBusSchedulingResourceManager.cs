// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0

using Lycia.Saga.Abstractions.Scheduling;
using Lycia.Saga.Abstractions;

namespace Lycia.Scheduling;

/// <summary>Adapts an event bus that exposes exact broker resource inspection to VacuumWorker.</summary>
public sealed class EventBusSchedulingResourceManager(IEventBus eventBus) : ISchedulingResourceManager
{
    private ISchedulingResourceManager? Inner => eventBus as ISchedulingResourceManager;

    /// <inheritdoc />
    public string TransportName => Inner?.TransportName ?? "unsupported";

    /// <inheritdoc />
    public Task<SchedulingResourceState> InspectAsync(SchedulingResourceRecord resource,
        CancellationToken cancellationToken = default) => Inner?.InspectAsync(resource, cancellationToken)
        ?? Task.FromResult(new SchedulingResourceState { Exists = false });

    /// <inheritdoc />
    public Task<bool> DeleteConditionallyAsync(SchedulingResourceRecord resource,
        CancellationToken cancellationToken = default) => Inner?.DeleteConditionallyAsync(resource, cancellationToken)
        ?? Task.FromResult(false);
}
