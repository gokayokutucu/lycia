using Autofac;
using Autofac.Extensions.DependencyInjection;
using Autofac.Integration.WebApi;
using Lycia.Extensions;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Reflection;
using System.Web.Http;
using System.Web.Mvc;
using System.Web.Optimization;
using System.Web.Routing;
using ConfigurationBuilder = Microsoft.Extensions.Configuration.ConfigurationBuilder;
using ConfigurationManager = System.Configuration.ConfigurationManager;

namespace Sample_Net48.Order.Choreography.Api
{
    public class WebApiApplication : System.Web.HttpApplication
    {
        protected void Application_Start()
        {
            var services = new ServiceCollection();
            services.AddLogging();

            var configBuilder = new ConfigurationBuilder()
                .AddInMemoryCollection(
                    ConfigurationManager.AppSettings.AllKeys
                        .ToDictionary(k => k, k => ConfigurationManager.AppSettings[k])
                );
            var configuration = configBuilder.Build();

            var lyciaServices = services.AddLycia(configuration);
            lyciaServices.AddSagasFromCurrentAssembly();

            // Build ServiceProvider
            var serviceProvider = services.BuildServiceProvider();

            // Autofac container builder
            var builder = new Autofac.ContainerBuilder();

            // Web API controllerlarını kaydet
            builder.RegisterApiControllers(Assembly.GetExecutingAssembly());

            // Microsoft.Extensions.DependencyInjection servislerini Autofac'e taşı
            builder.Populate(services);

            // Lycia'dan gelen singletonları birebir kaydetmek için serviceProvider üzerinden resolve et
            var eventBus = serviceProvider.GetRequiredService<IEventBus>();
            builder.RegisterInstance(eventBus).As<IEventBus>().SingleInstance();

            var container = builder.Build();

            // Autofac'i Web API'ye bağla
            var resolver = new AutofacWebApiDependencyResolver(container);
            GlobalConfiguration.Configuration.DependencyResolver = resolver;

            // Kalan Web API config
            AreaRegistration.RegisterAllAreas();
            GlobalConfiguration.Configure(WebApiConfig.Register);
            FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
            RouteConfig.RegisterRoutes(RouteTable.Routes);
            BundleConfig.RegisterBundles(BundleTable.Bundles);
        }

    }
}
