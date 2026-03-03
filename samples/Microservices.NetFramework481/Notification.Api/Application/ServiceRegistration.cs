using Autofac;
using Autofac.Extensions.DependencyInjection;
using FluentValidation;
using Lycia.Extensions;
using Lycia.Extensions.Logging;
using Lycia.Middleware;
using Mapster;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sample.Notification.NetFramework481.Application.Common.Behaviors;
using System;
using System.Reflection;

namespace Sample.Notification.NetFramework481.Application;

public static class ServiceRegistration
{
    public static void AddApplication(this ContainerBuilder builder, IConfiguration configuration)
    {
        var assembly = Assembly.GetExecutingAssembly();

        TypeAdapterConfig.GlobalSettings.Scan(assembly);

        builder.RegisterType<Mediator>()
            .As<IMediator>()
            .InstancePerLifetimeScope();

        builder.Register<IServiceProvider>(context =>
        {
            var c = context.Resolve<ILifetimeScope>();
            return new AutofacServiceProvider(c);
        }).InstancePerLifetimeScope();

        builder.RegisterAssemblyTypes(assembly)
            .AsClosedTypesOf(typeof(IRequestHandler<,>))
            .InstancePerLifetimeScope();

        builder.RegisterGeneric(typeof(ValidationBehavior<,>))
            .As(typeof(IPipelineBehavior<,>))
            .InstancePerLifetimeScope();

        builder.RegisterGeneric(typeof(ExceptionHandlingBehavior<,>))
            .As(typeof(IPipelineBehavior<,>))
            .InstancePerLifetimeScope();

        builder.RegisterAssemblyTypes(assembly)
            .Where(t => t.IsClosedTypeOf(typeof(IValidator<>)))
            .AsImplementedInterfaces()
            .InstancePerLifetimeScope();

        var services = new ServiceCollection();

        services
            .AddLycia(configuration)
            .UseSagaMiddleware(opt =>
            {
                opt.AddMiddleware<SerilogLoggingMiddleware>();
                opt.AddMiddleware<RetryMiddleware>();
            })
            .AddSagasFromCurrentAssembly()
            .Build();

        builder.Populate(services);
    }
}

