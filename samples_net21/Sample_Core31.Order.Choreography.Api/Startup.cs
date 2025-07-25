using Autofac;
using Autofac.Extensions.DependencyInjection;
using Autofac.Integration.WebApi;
using Lycia.Extensions;
using Lycia.Extensions.Configurations;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Configurations;
using Lycia.Saga.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sample_Net21.Shared.Messages.Commands;
using System.Reflection;

namespace Sample_Core31.Order.Choreography.Api
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers();
            // Add your services here
            services.AddSwaggerGen();
        }

        public void ConfigureContainer(ContainerBuilder builder)
        {
            var lyciaOptions = new LyciaOptions
            {
                EventBusProvider = Configuration["Lycia:EventBus:Provider"] ?? "RabbitMQ",
                EventStoreProvider = Configuration["Lycia:EventStore:Provider"] ?? "Redis",
                ApplicationId = Configuration["ApplicationId"],
                CommonTtlSeconds = int.TryParse(Configuration["Lycia:CommonTTL"], out var ttl) ? ttl : 60,
                EventStoreConnectionString = Configuration["Lycia:EventStore:ConnectionString"],
                EventBusConnectionString = Configuration["Lycia:EventBus:ConnectionString"]
            };
            builder.AddLycia(lyciaOptions);

            builder.AddSagasFromCurrentAssembly(lyciaOptions);

            builder.RegisterAssemblyTypes(Assembly.GetExecutingAssembly())
                   .Where(t => t.Name.EndsWith("Controller"))
                   .AsSelf();
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "Sample_Core31 Order Choreography Api V1");
                // c.RoutePrefix = string.Empty;
            });

            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }

}
