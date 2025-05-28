var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Register custom services & Lycia RabbitMQ Publisher
builder.Services.AddScoped<Sample.InventoryService.API.Services.StockReservationService>();

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

// Register Event Handler for OrderCreatedEvent
builder.Services.AddScoped<Lycia.Messaging.Abstractions.IEventHandler<Sample.InventoryService.API.Dtos.IncomingOrder.OrderCreatedEventDto>, Sample.InventoryService.API.EventHandlers.OrderCreatedEventHandler>();

// Register Event Handler for PaymentRefundedEventDto
builder.Services.AddScoped<Lycia.Messaging.Abstractions.IEventHandler<Sample.InventoryService.API.Dtos.IncomingPayment.PaymentRefundedEventDto>, Sample.InventoryService.API.EventHandlers.PaymentRefundedEventHandler>();


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
    subscriber.Subscribe<Sample.InventoryService.API.Dtos.IncomingOrder.OrderCreatedEventDto>(
        "inventory_service_order_created_q", 
        "saga_events_exchange", 
        "order.created");

    subscriber.Subscribe<Sample.InventoryService.API.Dtos.IncomingPayment.PaymentRefundedEventDto>(
        "inventory_service_saga_events_q", // Using the general saga events queue for this service
        "saga_events_exchange",
        "order.payment.refunded");
    
    subscriber.StartListening();

    // Ensure subscriber is disposed on application shutdown
    app.Lifetime.ApplicationStopping.Register(() => {
        var sub = app.Services.GetRequiredService<Lycia.Messaging.Abstractions.IMessageSubscriber>();
        sub.Dispose(); 
    });
    app.Logger.LogInformation("RabbitMQ IMessageSubscriber started and configured for OrderCreatedEventDto and PaymentRefundedEventDto.");
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
// using Sample.InventoryService.API.Services; // For AddScoped
// using Sample.InventoryService.API.EventHandlers; // For OrderCreatedEventHandler
// using Sample.InventoryService.API.Dtos.IncomingOrder; // For OrderCreatedEventDto
// using Microsoft.Extensions.DependencyInjection; // For GetRequiredService
// using Microsoft.Extensions.Hosting; // For ApplicationStopping
// using Lycia.Messaging.Abstractions; // For IMessageSubscriber, IEventHandler
// using Lycia.Saga; // For AddDefaultSagaIdGenerator
// using Sample.InventoryService.API.Dtos.IncomingPayment; // For PaymentRefundedEventDto
