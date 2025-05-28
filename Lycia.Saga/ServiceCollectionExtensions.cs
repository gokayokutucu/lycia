using Lycia.Saga.Abstractions;
using Lycia.Saga.Implementations.IdGeneration;
using Microsoft.Extensions.DependencyInjection;

namespace Lycia.Saga
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Adds the default ISagaIdGenerator implementation to the specified IServiceCollection.
        /// Registers <see cref="DefaultSagaIdGenerator"/> as a Singleton.
        /// </summary>
        /// <param name="services">The IServiceCollection to add services to.</param>
        /// <returns>The IServiceCollection so that additional calls can be chained.</returns>
        public static IServiceCollection AddDefaultSagaIdGenerator(this IServiceCollection services)
        {
            services.AddSingleton<ISagaIdGenerator, DefaultSagaIdGenerator>();
            return services;
        }
    }
}
