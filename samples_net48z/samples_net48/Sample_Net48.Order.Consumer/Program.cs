using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using Lycia.Saga.Extensions;
using Lycia.Extensions;

namespace Sample_Net48.Order.Consumer
{
    public static class Program
    {
        static void Main(string[] args)
        {
            var isService = !(Environment.UserInteractive || args.Contains("--console"));

            var builder = new HostBuilder()
                .UseContentRoot(AppDomain.CurrentDomain.BaseDirectory)
                .ConfigureAppConfiguration((hostContext, config) =>
                {
                    config.SetBasePath(AppDomain.CurrentDomain.BaseDirectory);
                    config.AddJsonFile("appsettings.json", optional: true);
                    config.AddEnvironmentVariables();
                })
                .ConfigureServices((hostContext, services) =>
                {
                    services
                        .AddLogging(configure => configure.AddConsole())
                        .AddLycia(hostContext.Configuration)
                        .AddSagasFromCurrentAssembly();
                });

            if (isService)
            {
                builder.UseWindowsService();
            }
            else
            {
                builder.UseConsoleLifetime();
            }

            var host = builder.Build();
            host.Run();
        }
    }
}
