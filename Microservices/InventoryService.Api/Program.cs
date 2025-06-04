using InventoryService.Api.Controllers;
using System.Reflection;
// Lycia specific using statements
using Lycia.Saga.Abstractions;
using Lycia.Saga.Extensions;
using Lycia.Infrastructure.Eventing;
using Lycia.Infrastructure.Stores;
using Lycia.Infrastructure.Dispatching;
using Lycia.Infrastructure.Abstractions; // For ISagaDispatcher
using InventoryService.Api.Messaging; // For InventoryEventRabbitMqListener

var builder = WebApplication.CreateBuilder(args);

// 1) Register MediatR v12 handlers by specifying which assembly(ies) to scan:
builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssemblies(
        Assembly.GetExecutingAssembly(),
        // Assuming InventoryService.Application assembly contains handlers including consumers
        typeof(InventoryService.Application.Features.Stocks.Commands.AddStockCommand).Assembly
    )
);

// Lycia Saga and EventBus registrations
builder.Services.AddSingleton<ISagaIdGenerator, DefaultSagaIdGenerator>();
// builder.Services.AddSingleton<IEventBus, InMemoryEventBus>(); // Replaced by RabbitMqMessageBroker
builder.Services.AddSingleton<ISagaStore, InMemorySagaStore>();
builder.Services.AddScoped<ISagaDispatcher, SagaDispatcher>();

// RabbitMQ Configuration for InventoryService to publish events
// It will use RabbitMqMessageBroker and RabbitMqOptions from OrderService.Infrastructure
builder.Services.Configure<OrderService.Infrastructure.Messaging.RabbitMqOptions>(builder.Configuration.GetSection("RabbitMq"));
builder.Services.AddSingleton<OrderService.Infrastructure.Messaging.RabbitMqMessageBroker>();
builder.Services.AddSingleton<IEventBus>(sp => sp.GetRequiredService<OrderService.Infrastructure.Messaging.RabbitMqMessageBroker>());
// Note: InventoryService does not define its own IMessageBroker for now, only uses IEventBus for saga events.

builder.Services.AddHostedService<InventoryEventRabbitMqListener>();

builder.AddServiceDefaults();

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapInventoryEndpoints();

//app.MapInventoryEndpoints(); // This line was duplicated in the original, removed one.

app.Run();
