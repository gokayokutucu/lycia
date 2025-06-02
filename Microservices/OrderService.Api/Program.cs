var builder = WebApplication.CreateBuilder(args);

using MediatR; // Assuming MediatR.Extensions.Microsoft.DependencyInjection is added
using OrderService.Application.Features.Orders.Commands.CreateOrder; // For MediatR assembly scanning
using OrderService.Application.Contracts.Persistence;
using OrderService.Infrastructure.Repositories;
using OrderService.Application.Contracts.Infrastructure;
using OrderService.Infrastructure.Messaging;
using Lycia.Extensions.Stores.Redis;
using StackExchange.Redis;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Extensions; // For DefaultSagaIdGenerator and Scan extensions
using Lycia.Infrastructure.Dispatching; // For SagaDispatcher
using Lycia.Infrastructure.Eventing;    // For InMemoryEventBus
using OrderService.Application.Features.Orders.Sagas.Handlers; // For StartOrderProcessingSagaHandler for Scan

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// 1. Configure Options
builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection("RabbitMq"));
builder.Services.Configure<RedisSagaStoreOptions>(builder.Configuration.GetSection("RedisSagaStore"));

// 2. Register MediatR
builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssembly(typeof(CreateOrderCommand).Assembly)); // Using CreateOrderCommand from Application layer

// 3. Register Application Services & Repositories
builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<IMessageBroker, RabbitMqMessageBroker>();

// 4. Register Redis Client (StackExchange.Redis)
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var configuration = builder.Configuration.GetSection("Redis").GetValue<string>("ConnectionString");
    if (string.IsNullOrEmpty(configuration))
    {
        throw new InvalidOperationException("Redis connection string ('Redis:ConnectionString') is not configured.");
    }
    return ConnectionMultiplexer.Connect(configuration);
});
builder.Services.AddScoped<IDatabase>(sp =>
{
    var multiplexer = sp.GetRequiredService<IConnectionMultiplexer>();
    return multiplexer.GetDatabase();
});

// 5. Register Lycia ISagaStore
builder.Services.AddScoped<ISagaStore, RedisSagaStore>();

// 6. Register Core Lycia Services
builder.Services.AddScoped<ISagaIdGenerator, DefaultSagaIdGenerator>();
builder.Services.AddScoped<ISagaDispatcher, SagaDispatcher>();
builder.Services.AddScoped<IEventBus, InMemoryEventBus>(sp =>
    new InMemoryEventBus(new Lazy<ISagaDispatcher>(() => sp.GetRequiredService<ISagaDispatcher>())));

// 7. Register Lycia Saga Handlers from OrderService.Application
// This uses Scrutor, which should be brought in by Lycia.Saga.Extensions or added explicitly if not.
// Ensuring StartOrderProcessingSagaHandler assembly is scanned.
builder.Services.Scan(scan => scan
    .FromAssemblies(typeof(StartOrderProcessingSagaHandler).Assembly)
    .AddClasses(classes => classes.AssignableTo(typeof(ISagaHandler<>)))
        .AsImplementedInterfaces()
        .WithScopedLifetime()
    .AddClasses(classes => classes.AssignableTo(typeof(ISagaHandler<,>)))
        .AsImplementedInterfaces()
        .WithScopedLifetime()
    .AddClasses(classes => classes.AssignableTo(typeof(ISagaStartHandler<,>)))
        .AsSelfWithInterfaces()
        .WithScopedLifetime()
    .AddClasses(classes => classes.AssignableTo(typeof(ISagaStartHandler<>)))
        .AsSelfWithInterfaces()
        .WithScopedLifetime()
    // Add other handler types if necessary (e.g., ISagaCompensationHandler)
    .AddClasses(classes => classes.AssignableTo(typeof(ISagaCompensationHandler<,>)))
        .AsImplementedInterfaces()
        .WithScopedLifetime()
);


builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddControllers(); // Add this for controller support

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapControllers(); // Add this to map attribute-routed controllers

// Placeholder for actual API endpoints - to be defined in a future step
app.MapGet("/", () => "OrderService.Api is running.");


app.Run();
