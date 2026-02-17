using Autofac;
using Autofac.Extras.DynamicProxy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Sample.Notification.NetFramework481.Application.Interfaces;
using Sample.Notification.NetFramework481.Infrastructure.Interceptors;
using Sample.Notification.NetFramework481.Infrastructure.Persistence;
using Sample.Notification.NetFramework481.Infrastructure.Persistence.Repositories;
using System;

namespace Sample.Notification.NetFramework481.Infrastructure;

public static class ServiceRegistration
{
    public static void AddInfrastructure(this ContainerBuilder builder, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("NotificationDB");

        builder.Register(c =>
        {
            var optionsBuilder = new DbContextOptionsBuilder<NotificationDbContext>();
            optionsBuilder.UseSqlServer(connectionString, sqlServerOptions =>
            {
                sqlServerOptions.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(30),
                    errorNumbersToAdd: null);
            });
            return new NotificationDbContext(optionsBuilder.Options);
        })
            .AsSelf()
            .InstancePerLifetimeScope();

        builder.RegisterType<RepositoryExceptionInterceptor>();

        builder.RegisterType<NotificationRepository>()
            .As<INotificationRepository>()
            .EnableInterfaceInterceptors()
            .InterceptedBy(typeof(RepositoryExceptionInterceptor))
            .InstancePerLifetimeScope();
    }
}
