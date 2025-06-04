using System.Reflection;
using OrderService.Api.Controllers;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Extensions;
using Lycia.Infrastructure.Eventing;
using Lycia.Infrastructure.Stores;
using Lycia.Infrastructure.Dispatching;
using Lycia.Infrastructure.Abstractions; // For ISagaDispatcher
using OrderService.Infrastructure.Messaging; // For RabbitMqOptions, RabbitMqMessageBroker
using OrderService.Infrastructure.Stores.Redis; // Updated for copied RedisSagaStore
using Microsoft.Extensions.Options; // For IOptions<>

var builder = WebApplication.CreateBuilder(args);

// 1) Register MediatR v12 handlers by specifying which assembly(ies) to scan:
builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssemblies(
        Assembly.GetExecutingAssembly(),
        // Assuming OrderService.Application assembly contains handlers including consumers
        typeof(OrderService.Application.Features.Orders.Commands.AddOrderCommand).Assembly
    )
);

// Lycia Saga and EventBus registrations
builder.Services.AddSingleton<ISagaIdGenerator, DefaultSagaIdGenerator>();
// builder.Services.AddSingleton<IEventBus, InMemoryEventBus>(); // Replaced by RabbitMqMessageBroker
// builder.Services.AddSingleton<ISagaStore, InMemorySagaStore>(); // Replaced by RedisSagaStore
builder.Services.AddScoped<ISagaDispatcher, SagaDispatcher>();

// Redis Configuration for Saga Store
builder.Services.Configure<OrderService.Infrastructure.Stores.Redis.RedisSagaStoreOptions>(options =>
{
    options.ConnectionString = builder.Configuration.GetConnectionString("Redis") ?? builder.Configuration.GetValue<string>("Redis:ConnectionString");
    // options.InstanceName = "OrderSagas_"; // Default is "sagas:" in RedisSagaStoreOptions if not set
});
builder.Services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(sp =>
{
    var options = sp.GetRequiredService<IOptions<OrderService.Infrastructure.Stores.Redis.RedisSagaStoreOptions>>().Value;
    if (string.IsNullOrEmpty(options.ConnectionString))
    {
        throw new InvalidOperationException("Redis connection string is not configured.");
    }
    return StackExchange.Redis.ConnectionMultiplexer.Connect(options.ConnectionString);
});
builder.Services.AddSingleton<ISagaStore, OrderService.Infrastructure.Stores.Redis.RedisSagaStore>();

// RabbitMQ Configuration
builder.Services.Configure<OrderService.Infrastructure.Messaging.RabbitMqOptions>(builder.Configuration.GetSection("RabbitMq"));
builder.Services.AddSingleton<RabbitMqMessageBroker>();
builder.Services.AddSingleton<IEventBus>(sp => sp.GetRequiredService<RabbitMqMessageBroker>());
builder.Services.AddSingleton<OrderService.Application.Contracts.Infrastructure.IMessageBroker>(sp => sp.GetRequiredService<RabbitMqMessageBroker>());


builder.AddServiceDefaults();

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Register RabbitMQ Listener Hosted Service
builder.Services.AddHostedService<OrderService.Api.Messaging.OrderEventRabbitMqListener>();

var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapOrderEndpoints();

app.Run();