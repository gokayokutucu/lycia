using Autofac;
using Autofac.Extras.DynamicProxy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Sample.Payment.NetFramework481.Application.Interfaces;
using Sample.Payment.NetFramework481.Infrastructure.Interceptors;
using Sample.Payment.NetFramework481.Infrastructure.Persistence;
using Sample.Payment.NetFramework481.Infrastructure.Persistence.Repositories;
using System;

namespace Sample.Payment.NetFramework481.Infrastructure;

public static class ServiceRegistration
{
    public static void AddInfrastructure(this ContainerBuilder builder, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("PaymentDB");

        builder.Register(c =>
        {
            var optionsBuilder = new DbContextOptionsBuilder<PaymentDbContext>();
            optionsBuilder.UseSqlServer(connectionString, sqlServerOptions =>
            {
                sqlServerOptions.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(30),
                    errorNumbersToAdd: null);
            });
            return new PaymentDbContext(optionsBuilder.Options);
        })
            .AsSelf()
            .InstancePerLifetimeScope();

        builder.RegisterType<RepositoryExceptionInterceptor>();

        builder.RegisterType<PaymentRepository>()
            .As<IPaymentRepository>()
            .EnableInterfaceInterceptors()
            .InterceptedBy(typeof(RepositoryExceptionInterceptor))
            .InstancePerLifetimeScope();
    }
}
