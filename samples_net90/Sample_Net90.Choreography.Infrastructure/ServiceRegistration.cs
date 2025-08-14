using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sample_Net90.Choreography.Application.Interfaces.Repositories;
using Sample_Net90.Choreography.Application.Interfaces.Services;
using Sample_Net90.Choreography.Infrastructure.Persistence;
using Sample_Net90.Choreography.Infrastructure.Repositories;
using Sample_Net90.Choreography.Infrastructure.Services;

namespace Sample_Net90.Choreography.Infrastructure;

public static class ServiceRegistration
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(
                configuration.GetConnectionString("DefaultConnection"),
                sql => sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)
            ));

        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IStockRepository, StockRepository>();
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();

        services.AddScoped<IDeliveryService, DeliveryService>();
        services.AddScoped<IPaymentService, PaymentService>();

        return services;
    }
}
