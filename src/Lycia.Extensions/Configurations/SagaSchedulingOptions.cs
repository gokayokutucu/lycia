// TargetFramework: netstandard2.0
using System;
using Microsoft.Extensions.Configuration;            // only for delegate type
using Microsoft.Extensions.DependencyInjection;       // only for delegate type

namespace Lycia.Extensions.Configurations
{
    /// <summary>
    /// Provider-agnostic scheduling options. No backend-specific helpers here.
    /// Defaults are NOT set here; ConfigureScheduling will supply them.
    /// </summary>
    public sealed class SagaSchedulingOptions
    {
        /// <summary>Logical provider name (e.g., "Redis", "Sql", "Memcached").</summary>
        public string? Provider { get; set; }

        /// <summary>AppSettings section for backend-specific settings (e.g., connection string).</summary>
        public string? StorageConfigSection { get; set; }

        /// <summary>Optional poll interval for the in-process scheduler loop.</summary>
        public TimeSpan? PollInterval { get; set; }

        /// <summary>Optional batch size for dequeuing due entries.</summary>
        public int? BatchSize { get; set; }

        /// <summary>
        /// Determines whether the scheduling loop should automatically start upon initialization.
        /// </summary>
        public bool? AutoStartLoop { get; set; }

        /// <summary>
        /// Optional hook to register a completely custom backend.
        /// If set, ConfigureScheduling will invoke it to wire services.
        /// </summary>
        public Action<IServiceCollection, IConfiguration, SagaSchedulingOptions>? ConfigureBackend { get; set; }
        

        // ---- Provider-agnostic fluent helpers ----
        public SagaSchedulingOptions UseProvider(string provider, string? configSection)
        {
            Provider = provider; 
            StorageConfigSection = configSection; 
            return this;
        }

        public SagaSchedulingOptions UseStorage(string configSection)
        {
            StorageConfigSection = configSection; 
            return this;
        }

        public SagaSchedulingOptions SetPollInterval(TimeSpan value)
        {
            PollInterval = value; 
            return this;
        }

        public SagaSchedulingOptions SetBatchSize(int value)
        {
            BatchSize = value; 
            return this;
        }
        
        public SagaSchedulingOptions SetAutoStartLoop(bool value)
        {
            AutoStartLoop = value; 
            return this;
        }

        public SagaSchedulingOptions WithBackend(
            Action<IServiceCollection, IConfiguration, SagaSchedulingOptions> configure)
        {
            ConfigureBackend = configure; 
            return this;
        }
    }
}