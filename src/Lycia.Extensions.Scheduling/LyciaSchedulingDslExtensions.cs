// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0

using Lycia.Extensions;
using Lycia.Scheduling;
using Microsoft.Extensions.DependencyInjection;

namespace Lycia.Extensions.Scheduling;

/// <summary>
/// Contributes the scheduling DSL to <see cref="LyciaBuilder"/>. Lycia.Extensions never depends on
/// Lycia.Extensions.Scheduling; this package extends <see cref="LyciaBuilder"/> with
/// <see cref="AddScheduling"/> the same way transport packages extend <see cref="LyciaTransportBuilder"/>.
/// </summary>
public static class LyciaSchedulingDslExtensions
{
    /// <summary>Starts the scheduling DSL: <c>lycia.AddScheduling().WithRedisStore().WithPredefinedDelays()...</c>.</summary>
    public static LyciaSchedulingBuilder AddScheduling(this LyciaBuilder lycia)
    {
        if (lycia == null) throw new ArgumentNullException(nameof(lycia));

#pragma warning disable CS0618 // internal call to the (obsolete-marked) shared registration entry point
        lycia.Services.AddLyciaScheduling();
#pragma warning restore CS0618
        return new LyciaSchedulingBuilder(lycia.Services);
    }
}

/// <summary>
/// Fluent scheduling DSL reached via <see cref="LyciaSchedulingDslExtensions.AddScheduling"/>. Delegates to
/// the existing <see cref="SchedulingOptions"/> configuration mechanism; it does not implement a second
/// scheduling registration path.
/// </summary>
public sealed class LyciaSchedulingBuilder
{
    private readonly IServiceCollection _services;

    internal LyciaSchedulingBuilder(IServiceCollection services) => _services = services;

    /// <summary>
    /// Uses the Redis-backed durable schedule store. This is the store <c>AddScheduling()</c> already
    /// registers, so calling this is only required to be explicit.
    /// </summary>
    public LyciaSchedulingBuilder WithRedisStore() => this;

    /// <summary>Switches to the deterministic in-memory schedule store, for tests and single-process development.</summary>
    public LyciaSchedulingBuilder WithInMemoryStore()
    {
#pragma warning disable CS0618
        _services.AddLyciaInMemoryScheduling();
#pragma warning restore CS0618
        return this;
    }

    /// <summary>Eagerly declares predefined-delay topology and disallows arbitrary dynamic-delay resources.</summary>
    public LyciaSchedulingBuilder WithPredefinedDelays()
    {
        _services.Configure<SchedulingOptions>(o => o.AllowDynamicDelays = false);
        return this;
    }

    /// <summary>Allows arbitrary delays to create dynamic transport-native resources.</summary>
    public LyciaSchedulingBuilder WithDynamicDelays()
    {
        _services.Configure<SchedulingOptions>(o => o.AllowDynamicDelays = true);
        return this;
    }

    /// <summary>Configures the durable <see cref="SchedulerWorker"/> (polling, leases, dispatch retry).</summary>
    public LyciaSchedulingBuilder WithWorker(Action<SchedulerWorkerOptions> configure)
    {
        if (configure == null) throw new ArgumentNullException(nameof(configure));
        _services.Configure<SchedulingOptions>(o => configure(o.Worker));
        return this;
    }

    /// <summary>Configures scheduling-resource and application-topology vacuum behavior.</summary>
    public LyciaSchedulingBuilder WithVacuum(Action<VacuumOptions> configure)
    {
        if (configure == null) throw new ArgumentNullException(nameof(configure));
        _services.Configure<SchedulingOptions>(o => configure(o.Vacuum));
        return this;
    }
}
