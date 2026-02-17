using Autofac;
using Autofac.Integration.WebApi;
using Lycia.Extensions.Listener;
using Microsoft.Extensions.Configuration;
using Microsoft.Owin;
using Owin;
using Sample.Notification.NetFramework481.API.Middleware;
using Sample.Notification.NetFramework481.Application;
using Sample.Notification.NetFramework481.Infrastructure;
using Serilog;
using Swashbuckle.Application;
using System;
using System.IO;
using System.Reflection;
using System.Web.Http;

[assembly: OwinStartup(typeof(Sample.Notification.NetFramework481.API.Startup))]

namespace Sample.Notification.NetFramework481.API;

public sealed class Startup
{
    public void Configuration(IAppBuilder app)
    {
        Log.Logger = new LoggerConfiguration()
            .Enrich.WithProperty("Service", "Notification.Api")
            .Enrich.FromLogContext()
            .Enrich.WithMachineName()
            .Enrich.WithThreadId()
            .Enrich.WithProcessId()
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{Service}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.Seq("http://localhost:5341")
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
            .MinimumLevel.Override("System", Serilog.Events.LogEventLevel.Warning)
            .CreateLogger();

        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables()
            .Build();

        var config = new HttpConfiguration();

        var builder = new ContainerBuilder();
        builder.AddInfrastructure(configuration);
        builder.AddApplication(configuration);

        builder.RegisterApiControllers(Assembly.GetExecutingAssembly());

        var container = builder.Build();
        config.DependencyResolver = new AutofacWebApiDependencyResolver(container);

        var scope = container.BeginLifetimeScope();
        try
        {
            var dbContext = scope.Resolve<Infrastructure.Persistence.NotificationDbContext>();
            Infrastructure.Persistence.DbInitializer.InitializeAsync(dbContext).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Database initialization failed during startup");
            scope?.Dispose();
            throw;
        }

        var listener = container.Resolve<RabbitMqListener>();

        config.MapHttpAttributeRoutes();

        config.Filters.Add(new Filters.GlobalExceptionFilter());
        config.Filters.Add(new Filters.LoggingFilter());
        config.Filters.Add(new Filters.GatewayOnlyFilter());

        config.Formatters.Remove(config.Formatters.XmlFormatter);

        config.EnableSwagger(c =>
        {
            c.SingleApiVersion("v1", "Notification API");
            c.DescribeAllEnumsAsStrings();

            var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
            var xmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, xmlFile);
            if (File.Exists(xmlPath))
                c.IncludeXmlComments(xmlPath);
        });

        app.Use<GlobalExceptionMiddleware>();

        app.UseWebApi(config);

        Log.Information("Notification.API started successfully");
    }
}
