using Lycia.Extensions;
using Lycia.Saga.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;
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

            var configBuilder = new ConfigurationBuilder()
            .AddInMemoryCollection(
                ConfigurationManager.AppSettings.AllKeys
                    .ToDictionary(k => k, k => ConfigurationManager.AppSettings[k])
            );
            var configuration = configBuilder.Build();

            var lyciaServices = services.AddLycia(configuration);
            lyciaServices.AddSagasFromCurrentAssembly();

            AreaRegistration.RegisterAllAreas();
            GlobalConfiguration.Configure(WebApiConfig.Register);
            FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
            RouteConfig.RegisterRoutes(RouteTable.Routes);
            BundleConfig.RegisterBundles(BundleTable.Bundles);
        }
    }
}
