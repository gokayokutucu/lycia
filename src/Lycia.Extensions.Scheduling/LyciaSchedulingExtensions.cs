// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0

using Lycia.Extensions.Configurations;
using Lycia.Saga.Abstractions.Scheduling;
using Lycia.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Lycia.Extensions.Scheduling;

/// <summary>Registers durable transport-independent Lycia scheduling services.</summary>
public static class LyciaSchedulingExtensions
{
    /// <summary>Registers Redis-backed scheduling, SchedulerWorker, and conservative vacuum defaults.</summary>
    [Obsolete("Use AddLycia(configuration, lycia => lycia.AddScheduling().WithRedisStore()...) instead.")]
    public static IServiceCollection AddLyciaScheduling(this IServiceCollection services,
        Action<SchedulingOptions>? configure = null)
    {
        if (services == null) throw new ArgumentNullException(nameof(services));
        services.AddOptions<SchedulingOptions>();
        if (configure != null) services.Configure(configure);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<SchedulingOptions>, SchedulingOptionsValidator>());
        RegisterCommon(services);
        services.TryAddSingleton<IScheduleStore>(provider =>
        {
            var connection = provider.GetRequiredService<IConnectionMultiplexer>();
            var applicationId = provider.GetRequiredService<IOptions<EventBusOptions>>().Value.ApplicationId;
            if (string.IsNullOrWhiteSpace(applicationId))
                throw new InvalidOperationException("Lycia:EventBus:ApplicationId is required for durable scheduling.");
            return new RedisScheduleStore(connection, applicationId!); // Guarded above for older nullable annotations.
        });
        services.TryAddSingleton<ISchedulingResourceRegistry, RedisSchedulingResourceRegistry>();
        services.TryAddSingleton<ITopologyManifestRegistry, RedisTopologyManifestRegistry>();
        services.TryAddSingleton<IVacuumLeaseManager, RedisVacuumLeaseManager>();
        return services;
    }

    /// <summary>Registers deterministic in-memory scheduling for tests and single-process development.</summary>
    [Obsolete("Use AddLycia(configuration, lycia => lycia.AddScheduling().WithInMemoryStore()...) instead.")]
    public static IServiceCollection AddLyciaInMemoryScheduling(this IServiceCollection services,
        Action<SchedulingOptions>? configure = null)
    {
        if (services == null) throw new ArgumentNullException(nameof(services));
        services.AddOptions<SchedulingOptions>();
        if (configure != null) services.Configure(configure);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<SchedulingOptions>, SchedulingOptionsValidator>());
        RegisterCommon(services);
        services.RemoveAll<IScheduleStore>();
        services.RemoveAll<ISchedulingResourceRegistry>();
        services.RemoveAll<ITopologyManifestRegistry>();
        services.RemoveAll<IVacuumLeaseManager>();
        services.AddSingleton<IScheduleStore, InMemoryScheduleStore>();
        services.AddSingleton<ISchedulingResourceRegistry, InMemorySchedulingResourceRegistry>();
        services.AddSingleton<ITopologyManifestRegistry, InMemoryTopologyManifestRegistry>();
        services.AddSingleton<IVacuumLeaseManager, InMemoryVacuumLeaseManager>();
        return services;
    }

    private static void RegisterCommon(IServiceCollection services)
    {
        services.TryAddSingleton<ISchedulingClock, SystemSchedulingClock>();
        services.TryAddSingleton<IMessageScheduler, MessageScheduler>();
        services.TryAddSingleton<ISchedulingDispatcher, EventBusSchedulingDispatcher>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ISchedulingResourceManager, EventBusSchedulingResourceManager>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Microsoft.Extensions.Hosting.IHostedService, SchedulerWorker>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Microsoft.Extensions.Hosting.IHostedService, VacuumWorker>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Microsoft.Extensions.Hosting.IHostedService, TopologyManifestWorker>());
        services.AddHealthChecks().AddCheck<SchedulingHealthCheck>("LyciaScheduling");
    }
}

/// <summary>Validates scheduling options before workers begin processing.</summary>
public sealed class SchedulingOptionsValidator : IValidateOptions<SchedulingOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, SchedulingOptions options)
    {
        if (options == null) return ValidateOptionsResult.Fail("Scheduling options are required.");
        var failures = new List<string>();
        if (options.PredefinedDelays == null) failures.Add("PredefinedDelays is required.");
        if (options.MaximumDelay <= TimeSpan.Zero) failures.Add("MaximumDelay must be positive.");
        if (options.ManifestHeartbeatInterval <= TimeSpan.Zero) failures.Add("ManifestHeartbeatInterval must be positive.");
        if (options.ManifestHeartbeatTimeout <= options.ManifestHeartbeatInterval)
            failures.Add("ManifestHeartbeatTimeout must be greater than ManifestHeartbeatInterval.");
        if (options.Worker.PollInterval <= TimeSpan.Zero) failures.Add("Worker.PollInterval must be positive.");
        if (options.Worker.BatchSize <= 0) failures.Add("Worker.BatchSize must be positive.");
        if (options.Worker.LeaseDuration <= TimeSpan.Zero) failures.Add("Worker.LeaseDuration must be positive.");
        if (options.Worker.LeaseRenewInterval <= TimeSpan.Zero)
            failures.Add("Worker.LeaseRenewInterval must be positive.");
        if (options.Worker.LeaseRenewInterval >= options.Worker.LeaseDuration)
            failures.Add("Worker.LeaseRenewInterval must be shorter than Worker.LeaseDuration.");
        if (options.Worker.MaxDispatchAttempts <= 0) failures.Add("Worker.MaxDispatchAttempts must be positive.");
        if (options.Worker.RetryBackoff < TimeSpan.Zero) failures.Add("Worker.RetryBackoff cannot be negative.");
        if (options.Worker.ShutdownDrainTimeout <= TimeSpan.Zero)
            failures.Add("Worker.ShutdownDrainTimeout must be positive.");
        if (options.Vacuum.SchedulingResources.Interval <= TimeSpan.Zero)
            failures.Add("Vacuum.SchedulingResources.Interval must be positive.");
        if (options.Vacuum.SchedulingResources.DynamicResourceRetention < TimeSpan.Zero)
            failures.Add("Vacuum.SchedulingResources.DynamicResourceRetention cannot be negative.");
        if (options.Vacuum.SchedulingResources.MinimumResourceAge < TimeSpan.Zero)
            failures.Add("Vacuum.SchedulingResources.MinimumResourceAge cannot be negative.");
        if (options.Vacuum.SchedulingResources.BatchSize <= 0)
            failures.Add("Vacuum.SchedulingResources.BatchSize must be positive.");
        if (options.Vacuum.ApplicationTopology.OrphanThreshold < TimeSpan.Zero)
            failures.Add("Vacuum.ApplicationTopology.OrphanThreshold cannot be negative.");
        if (options.Vacuum.ApplicationTopology.QuarantinePeriod < TimeSpan.Zero)
            failures.Add("Vacuum.ApplicationTopology.QuarantinePeriod cannot be negative.");
        if (options.Vacuum.ApplicationTopology.Mode == VacuumMode.Automatic &&
            !options.Vacuum.ApplicationTopology.AllowDestructiveApplicationTopologyCleanup)
            failures.Add("Automatic application-topology cleanup requires AllowDestructiveApplicationTopologyCleanup=true.");
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
