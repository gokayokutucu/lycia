using System.Reflection;
using Lycia.Dapr.EventBus;
using Lycia.Dapr.EventBus.Abstractions;
using Lycia.Dapr.Messages.Abstractions;
using Microsoft.Extensions.Configuration;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Provides extension methods for <see cref="T:IServiceCollection" />.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddDaprServiceBus(this IServiceCollection services, Assembly assembly)
        {
            var eventHandlerTypes = assembly.GetTypes()
                .Where(type => type.IsClass && !type.IsAbstract && typeof(IEventHandler).IsAssignableFrom(type));

            foreach (var eventHandlerType in eventHandlerTypes)
            {
                services.AddScoped(eventHandlerType);
            }
            
            services.AddSingleton<IEventBus, DaprEventBus>();
            services.AddDaprClient();

            return services;
        }
        
        /// <summary>
        /// Adds DaprEventBus services to the provided <see cref="T:IServiceCollection" />.
        /// </summary>
        /// <param name="services">The <see cref="T:IServiceCollection" /></param>
        /// <param name="configuration">The application's <see cref="IConfiguration"/>.</param>
        /// <param name="useSchemaRegistry">True to use schema registry</param>
        /// <returns>The original <see cref="T:IServiceCollection" />.</returns>
        [Obsolete("This version of AddDaprEventBus is obsolete. Instead use the version that omits the useSchemaRegistry parameter.", true)]
        public static IServiceCollection AddDaprEventBus(this IServiceCollection services,
            IConfiguration configuration, bool useSchemaRegistry) =>
            AddDaprEventBus(services, configuration);

        /// <summary>
        /// Adds DaprEventBus services to the provided <see cref="T:IServiceCollection" />.
        /// </summary>
        /// <param name="services">The <see cref="T:IServiceCollection" /></param>
        /// <param name="configuration">The application's <see cref="IConfiguration"/>.</param>
        /// <returns>The original <see cref="T:IServiceCollection" />.</returns>
        public static IServiceCollection AddDaprEventBus(this IServiceCollection services, IConfiguration configuration)
        {
            // var daprEventBusOptions = new DaprEventBusOptions();
            // var daprOptionsConfigSection = configuration.GetSection(nameof(DaprEventBusOptions));
            // daprOptionsConfigSection.Bind(daprEventBusOptions);
            // if (!daprOptionsConfigSection.Exists())
            //     throw new Exception($"Configuration section '{nameof(DaprEventBusOptions)}' not present in app settings.");
            // services.Configure<DaprEventBusOptions>(daprOptionsConfigSection);
            //
            // Action<DaprEventBusSchemaOptions>? configureSchemaOptions = null;
            // var eventBusSchemaOptions = new DaprEventBusSchemaOptions();
            // var schemaConfigSection = configuration.GetSection(nameof(DaprEventBusSchemaOptions));
            // schemaConfigSection.Bind(eventBusSchemaOptions);
            // if (schemaConfigSection.Exists())
            // {
            //     configureSchemaOptions = options =>
            //     {
            //         options.UseSchemaRegistry = eventBusSchemaOptions.UseSchemaRegistry;
            //         options.SchemaRegistryType = eventBusSchemaOptions.SchemaRegistryType;
            //         options.MongoStateStoreOptions = eventBusSchemaOptions.MongoStateStoreOptions;
            //         options.SchemaValidatorType = eventBusSchemaOptions.SchemaValidatorType;
            //         options.AddSchemaOnPublish = eventBusSchemaOptions.AddSchemaOnPublish;
            //     };
            // }
            // return services.AddDaprEventBus(daprEventBusOptions.PubSubName, configureSchemaOptions);
            return services;
        }
    }
}
