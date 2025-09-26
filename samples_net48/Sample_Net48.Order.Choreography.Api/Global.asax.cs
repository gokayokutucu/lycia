using Autofac;
using Autofac.Extensions.DependencyInjection;
using Autofac.Integration.WebApi;
using Lycia.Extensions;
using Lycia.Extensions.Logging;
using Lycia.Infrastructure.Middleware;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Reflection;
using System.Web.Http;
using System.Web.Routing;

namespace Sample_Net48.Order.Choreography.Api
{
    public class WebApiApplication : System.Web.HttpApplication
    {
        protected void Application_Start()
        {
            var basePath = AppDomain.CurrentDomain.BaseDirectory;
            var env = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "stage";

            var configuration = new ConfigurationBuilder()
                .SetBasePath(basePath)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile($"appsettings.{env.ToLower()}.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();

            var builder = new ContainerBuilder();

            var asm = Assembly.GetExecutingAssembly();
            builder.RegisterApiControllers(asm);
            builder.RegisterWebApiFilterProvider(GlobalConfiguration.Configuration);
            builder.RegisterWebApiModelBinderProvider();

            builder.RegisterInstance(configuration).As<IConfiguration>().SingleInstance();

            var loggerFactory = LoggerFactory.Create(b =>
            {
                b.AddDebug();
                b.SetMinimumLevel(LogLevel.Debug);
            });
            builder.RegisterInstance(loggerFactory).As<ILoggerFactory>().SingleInstance();
            builder.RegisterGeneric(typeof(Logger<>)).As(typeof(ILogger<>)).SingleInstance();

            builder
                .AddLycia(configuration)
                .UseSagaMiddleware(opt =>
                {
                    opt.AddMiddleware<SerilogLoggingMiddleware>();
                    opt.AddMiddleware<RetryMiddleware>();
                })
                .AddSagasFromCurrentAssembly()
                .Build();

            //builder.Register(ctx => new AutofacServiceProvider(ctx.Resolve<ILifetimeScope>()))
            //   .As<IServiceProvider>()
            //   .InstancePerLifetimeScope();

            var container = builder.Build();

            var resolver = new AutofacWebApiDependencyResolver(container);
            GlobalConfiguration.Configuration.DependencyResolver = resolver;

            GlobalConfiguration.Configure(WebApiConfig.Register);
            RouteConfig.RegisterRoutes(RouteTable.Routes);
        }
    }
}
