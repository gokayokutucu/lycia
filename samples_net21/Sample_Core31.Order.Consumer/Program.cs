using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Lycia.Extensions;
using Lycia.Saga.Configurations;
using Lycia.Saga.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Sample_Core31.Order.Consumer
{
        public class Program
        {
            public static void Main(string[] args)
            {
                var host = Host.CreateDefaultBuilder(args)
                    .UseServiceProviderFactory(new AutofacServiceProviderFactory())
                    .ConfigureAppConfiguration((context, config) =>
                    {
                        config.AddJsonFile("appsettings.json", optional: true)
                              .AddEnvironmentVariables();
                    })
                    .ConfigureServices((hostContext, services) =>
                    {
                        services.AddHostedService<Worker>();
                    })
                    .ConfigureContainer<ContainerBuilder>((hostContext, builder) =>
                    {
                        var config = hostContext.Configuration;
                        var lyciaOptions = new LyciaOptions
                        {
                            EventBusProvider = config["Lycia:EventBus:Provider"] ?? "RabbitMQ",
                            EventStoreProvider = config["Lycia:EventStore:Provider"] ?? "Redis",
                            ApplicationId = config["ApplicationId"],
                            CommonTtlSeconds = int.TryParse(config["Lycia:CommonTTL"], out var ttl) ? ttl : 60,
                            EventStoreConnectionString = config["Lycia:EventStore:ConnectionString"],
                            EventBusConnectionString = config["Lycia:EventBus:ConnectionString"]
                        };
                        builder.AddLycia(lyciaOptions);

                        // Register saga handlers from this assembly
                        builder.AddSagasFromCurrentAssembly(lyciaOptions);

                        // Register worker if you want Autofac to manage its constructor params
                        builder.RegisterType<Worker>().AsSelf();
                    })
                    .Build();

                host.Run();
            }
    }
}
