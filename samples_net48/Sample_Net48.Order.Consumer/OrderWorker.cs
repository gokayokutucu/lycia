using Lycia.Extensions;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.ServiceProcess;
using ConfigurationBuilder = Microsoft.Extensions.Configuration.ConfigurationBuilder;
using ConfigurationManager = System.Configuration.ConfigurationManager;

namespace Sample_Net48.Order.Consumer
{
    public partial class OrderWorker : ServiceBase
    {
        private IServiceProvider _serviceProvider;
        public OrderWorker()
        {
            InitializeComponent();
        }

        protected override void OnStart(string[] args)
        {
            var services = new ServiceCollection();

            // App.config -> IConfiguration'a dönüştür
            var configBuilder = new ConfigurationBuilder()
                .AddInMemoryCollection(
                    ConfigurationManager.AppSettings.AllKeys
                        .ToDictionary(k => k, k => ConfigurationManager.AppSettings[k])
                );

            var configuration = configBuilder.Build();

            // Lycia servislerini ekle
            var lyciaServices = services.AddLycia(configuration);
            lyciaServices.AddSagasFromCurrentAssembly();

            // Autofac yerine ServiceProvider
            _serviceProvider = services.BuildServiceProvider();

            // İstediğin servisleri resolve edebilirsin
            var eventBus = _serviceProvider.GetRequiredService<IEventBus>();
            // eventBus.Subscribe(...) gibi kullanım yapılabilir
        }

        protected override void OnStop()
        {
        }
    }
}
