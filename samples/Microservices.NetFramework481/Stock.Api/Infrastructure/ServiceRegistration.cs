using Autofac;
using Autofac.Extras.DynamicProxy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Sample.Product.NetFramework481.Application.Interfaces;
using Sample.Product.NetFramework481.Infrastructure.Interceptors;
using Sample.Product.NetFramework481.Infrastructure.Persistence;
using Sample.Product.NetFramework481.Infrastructure.Persistence.Repositories;
using System;

namespace Sample.Product.NetFramework481.Infrastructure;

public static class ServiceRegistration
{
    public static void AddInfrastructure(this ContainerBuilder builder, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("ProductDB");

        builder.Register(c =>
        {
            var optionsBuilder = new DbContextOptionsBuilder<ProductDbContext>();
            optionsBuilder.UseSqlServer(connectionString, sqlServerOptions =>
            {
                sqlServerOptions.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(30),
                    errorNumbersToAdd: null);
            });
            return new ProductDbContext(optionsBuilder.Options);
        })
            .AsSelf()
            .InstancePerLifetimeScope();

        builder.RegisterType<RepositoryExceptionInterceptor>();

        builder.RegisterType<ProductRepository>()
            .As<IProductRepository>()
            .EnableInterfaceInterceptors()
            .InterceptedBy(typeof(RepositoryExceptionInterceptor))
            .InstancePerLifetimeScope();
    }
}
