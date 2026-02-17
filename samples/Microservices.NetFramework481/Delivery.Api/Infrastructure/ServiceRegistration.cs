using Autofac;
using Autofac.Extras.DynamicProxy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Sample.Delivery.NetFramework481.Application.Interfaces;
using Sample.Delivery.NetFramework481.Infrastructure.Interceptors;
using Sample.Delivery.NetFramework481.Infrastructure.Persistence;
using Sample.Delivery.NetFramework481.Infrastructure.Persistence.Repositories;
using System;

namespace Sample.Delivery.NetFramework481.Infrastructure;

public static class ServiceRegistration
{
    public static void AddInfrastructure(this ContainerBuilder builder, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DeliveryDB");

        builder.Register(c =>
        {
            var optionsBuilder = new DbContextOptionsBuilder<DeliveryDbContext>();
            optionsBuilder.UseSqlServer(connectionString, sqlServerOptions =>
            {
                sqlServerOptions.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(30),
                    errorNumbersToAdd: null);
            });
            return new DeliveryDbContext(optionsBuilder.Options);
        })
            .AsSelf()
            .InstancePerLifetimeScope();

        builder.RegisterType<RepositoryExceptionInterceptor>();

        builder.RegisterType<DeliveryRepository>()
            .As<IDeliveryRepository>()
            .EnableInterfaceInterceptors()
            .InterceptedBy(typeof(RepositoryExceptionInterceptor))
            .InstancePerLifetimeScope();
    }
}
