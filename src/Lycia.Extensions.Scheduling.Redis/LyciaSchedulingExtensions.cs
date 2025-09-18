// TargetFramework: netstandard2.0

using Lycia.Extensions.Configurations;
using Lycia.Saga.Abstractions;
using Lycia.Scheduling;
using Lycia.Scheduling.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace Lycia.Extensions.Scheduling.Redis
{
    public static class LyciaSchedulingExtensions
    {
        public static LyciaBuilder ConfigureScheduling(this LyciaBuilder builder, Action<SagaSchedulingOptions>? configuration)
        {
            builder.ThrowIfNotInline("ConfigureScheduling");
            if (configuration == null) return builder;

            var opts = new SagaSchedulingOptions();
            configuration(opts);

            // Method-owned defaults
            var provider       = string.IsNullOrWhiteSpace(opts.Provider)            ? "Redis"               : opts.Provider.Trim();
            var storageSection = string.IsNullOrWhiteSpace(opts.StorageConfigSection) ? "Lycia:Scheduling"    : opts.StorageConfigSection.Trim();
            var pollInterval   = opts.PollInterval ?? TimeSpan.FromSeconds(1);
            var batchSize      = opts.BatchSize    ?? 100;

            var services = builder.Services;
            var config   = builder.ConfigurationRoot;

            // reflect resolved defaults back to options (so custom registrar can see them)
            opts.Provider = provider;
            opts.StorageConfigSection = storageSection;
            opts.PollInterval = pollInterval;
            opts.BatchSize = batchSize;

            if (opts.ConfigureBackend != null)
            {
                opts.ConfigureBackend(services, config, opts);
            }
            else
            {
                switch (provider.ToLowerInvariant())
                {
                    case "redis":
                    {
                        var section = config.GetSection(storageSection);
                        var conn    = section["ConnectionString"];
                        var appId   = config["ApplicationId"] ?? "App";
                        if (string.IsNullOrWhiteSpace(conn))
                            throw new InvalidOperationException(storageSection + ":ConnectionString is required.");

                        services.TryAddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(conn));
                        services.TryAddSingleton<IScheduleStorage>(sp => new RedisScheduleStorage(sp.GetRequiredService<IConnectionMultiplexer>(), appId));
                        services.TryAddSingleton<IScheduler, DefaultScheduler>();
                        break;
                    }
                    default:
                        throw new NotSupportedException("Scheduling provider '" + provider + "' is not supported. Use WithBackend(...) to register a custom backend.");
                }
            }

            // Loop with resolved defaults
            services.RemoveAll(typeof(SchedulerLoop));
            services.AddSingleton(sp => new SchedulerLoop(
                sp.GetRequiredService<IScheduleStorage>(),
                sp.GetRequiredService<IEventBus>(),
                sp.GetRequiredService<IMessageSerializer>(),
                pollInterval,
                batchSize
            ));

            if (opts.AutoStartLoop ?? true)
            {
                services.AddHostedService<SchedulerHostedService>();
            }

            return builder;
        }
    }
}