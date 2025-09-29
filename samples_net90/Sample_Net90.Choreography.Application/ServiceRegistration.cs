using FluentValidation;
using Lycia.Extensions;
using Mapster;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sample_Net90.Choreography.Application.Behaviors;
using Sample_Net90.Choreography.Application.Order.Commands.Create;
using System.Reflection;

namespace Sample_Net90.Choreography.Application;

public static class ServiceRegistration
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration? configuration)
    {
        services.AddLycia(configuration)
            .AddSagasFromCurrentAssembly();

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<CreateOrderCommandHandler>());

        services.AddValidatorsFromAssemblyContaining<CreateOrderCommandValidator>();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        services.AddMapster();
        TypeAdapterConfig.GlobalSettings.Scan(Assembly.GetExecutingAssembly());

        return services;
    }
}
