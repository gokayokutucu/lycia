using Autofac;
using Autofac.Extras.DynamicProxy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Sample.Order.NetFramework481.Application.Interfaces;
using Sample.Order.NetFramework481.Infrastructure.Interceptors;
using Sample.Order.NetFramework481.Infrastructure.Persistence;
using Sample.Order.NetFramework481.Infrastructure.Persistence.Repositories;
using System;

namespace Sample.Order.NetFramework481.Infrastructure;

public static class ServiceRegistration
{
    public static void AddInfrastructure(this ContainerBuilder builder, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("OrderDB");

        builder.Register(c =>
            {
                var optionsBuilder = new DbContextOptionsBuilder<OrderDbContext>();
                optionsBuilder.UseSqlServer(connectionString, sqlServerOptions =>
                {
                    sqlServerOptions.EnableRetryOnFailure(
                        maxRetryCount: 5,
                        maxRetryDelay: TimeSpan.FromSeconds(30),
                        errorNumbersToAdd: null);
                });
                return new OrderDbContext(optionsBuilder.Options);
            })
            .AsSelf()
            .InstancePerLifetimeScope();

        builder.RegisterType<RepositoryExceptionInterceptor>();

        builder.RegisterType<OrderRepository>()
            .As<IOrderRepository>()
            .EnableInterfaceInterceptors()
            .InterceptedBy(typeof(RepositoryExceptionInterceptor))
            .InstancePerLifetimeScope();

        builder.RegisterType<CustomerRepository>()
            .As<ICustomerRepository>()
            .EnableInterfaceInterceptors()
            .InterceptedBy(typeof(RepositoryExceptionInterceptor))
            .InstancePerLifetimeScope();

        builder.RegisterType<AddressRepository>()
            .As<IAddressRepository>()
            .EnableInterfaceInterceptors()
            .InterceptedBy(typeof(RepositoryExceptionInterceptor))
            .InstancePerLifetimeScope();

        builder.RegisterType<CardRepository>()
            .As<ICardRepository>()
            .EnableInterfaceInterceptors()
            .InterceptedBy(typeof(RepositoryExceptionInterceptor))
            .InstancePerLifetimeScope();
    }
}
