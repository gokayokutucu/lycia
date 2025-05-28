var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Register custom services & Lycia RabbitMQ Publisher
builder.Services.AddScoped<Sample.PaymentService.API.Services.PaymentProcessingService>();

// This uses the extension method from Lycia.Infrastructure
// It will use the default connection string "amqp://guest:guest@localhost:5672"
// if no URI is provided and IConfiguration isn't set up to provide one.
builder.Services.AddRabbitMqPublisher(); // Registers IMessagePublisher, IRabbitMqChannelProvider

// Add Lycia Message Subscriber for consuming events
builder.Services.AddLyciaMessageSubscriber(); // From Lycia.Infrastructure

// Add Lycia Saga Core Services
builder.Services.AddDefaultSagaIdGenerator(); // From Lycia.Saga - Registers ISagaIdGenerator

// Add Lycia Infrastructure Services for Saga
builder.Services.AddRabbitMqEventBus(); // From Lycia.Infrastructure - Registers IEventBus (needed by RedisSagaStore context)
builder.Services.AddRedisSagaStore(builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379"); // Registers ISagaStore

// Register Event Handler for StockReservedEventDto
builder.Services.AddScoped<Lycia.Messaging.Abstractions.IEventHandler<Sample.PaymentService.API.Dtos.IncomingStock.StockReservedEventDto>, Sample.PaymentService.API.EventHandlers.StockReservedEventHandler>();

// Register Event Handler for ShipmentFailedEventDto
builder.Services.AddScoped<Lycia.Messaging.Abstractions.IEventHandler<Sample.PaymentService.API.Dtos.IncomingShipment.ShipmentFailedEventDto>, Sample.PaymentService.API.EventHandlers.ShipmentFailedEventHandler>();


var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(); // Added for testing
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

// Configure and start RabbitMQ subscriber
try
{
    var subscriber = app.Services.GetRequiredService<Lycia.Messaging.Abstractions.IMessageSubscriber>();
    subscriber.Subscribe<Sample.PaymentService.API.Dtos.IncomingStock.StockReservedEventDto>(
        "payment_service_stock_reserved_q", 
        "saga_events_exchange", 
        "order.stock.reserved");

    subscriber.Subscribe<Sample.PaymentService.API.Dtos.IncomingShipment.ShipmentFailedEventDto>(
        "payment_service_saga_events_q", // Using the general saga events queue for this service
        "saga_events_exchange",
        "order.shipment.failed");
    
    subscriber.StartListening();

    // Ensure subscriber is disposed on application shutdown
    app.Lifetime.ApplicationStopping.Register(() => {
        var sub = app.Services.GetRequiredService<Lycia.Messaging.Abstractions.IMessageSubscriber>();
        sub.Dispose(); 
    });
    app.Logger.LogInformation("RabbitMQ IMessageSubscriber started and configured for StockReservedEventDto and ShipmentFailedEventDto.");
}
catch (Exception ex)
{
    app.Logger.LogCritical(ex, "Failed to start or configure RabbitMQ IMessageSubscriber.");
    // Depending on policy, might rethrow or exit if subscriber is critical
}


app.Run();

// Required for Lycia.Infrastructure.ServiceCollectionExtensions
// if not already implicitly available through other usings or SDK.
// using Lycia.Infrastructure; 
// using Sample.PaymentService.API.Services; // For AddScoped
// using Sample.PaymentService.API.EventHandlers;
// using Sample.PaymentService.API.Dtos.IncomingStock;
// using Microsoft.Extensions.DependencyInjection;
// using Microsoft.Extensions.Hosting;
// using Lycia.Messaging.Abstractions;
// using Lycia.Saga; // For AddDefaultSagaIdGenerator
// using Sample.PaymentService.API.Dtos.IncomingShipment; // For ShipmentFailedEventDto
