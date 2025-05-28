var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Register custom services & Lycia RabbitMQ Publisher
builder.Services.AddScoped<Sample.DeliveryService.API.Services.ShipmentSchedulingService>();

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

// Register Event Handler for PaymentProcessedEventDto
builder.Services.AddScoped<Lycia.Messaging.Abstractions.IEventHandler<Sample.DeliveryService.API.Dtos.IncomingPayment.PaymentProcessedEventDto>, Sample.DeliveryService.API.EventHandlers.PaymentProcessedEventHandler>();


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
    subscriber.Subscribe<Sample.DeliveryService.API.Dtos.IncomingPayment.PaymentProcessedEventDto>(
        "delivery_service_payment_processed_q", 
        "saga_events_exchange", 
        "order.payment.processed");
    
    subscriber.StartListening();

    // Ensure subscriber is disposed on application shutdown
    app.Lifetime.ApplicationStopping.Register(() => {
        var sub = app.Services.GetRequiredService<Lycia.Messaging.Abstractions.IMessageSubscriber>();
        sub.Dispose(); 
    });
    app.Logger.LogInformation("RabbitMQ IMessageSubscriber started and configured for PaymentProcessedEventDto.");
}
catch (Exception ex)
{
    app.Logger.LogCritical(ex, "Failed to start or configure RabbitMQ IMessageSubscriber for PaymentProcessedEventDto.");
    // Depending on policy, might rethrow or exit if subscriber is critical
}


app.Run();

// Required for Lycia.Infrastructure.ServiceCollectionExtensions
// if not already implicitly available through other usings or SDK.
// using Lycia.Infrastructure; 
// using Sample.DeliveryService.API.Services; // For AddScoped
// using Sample.DeliveryService.API.EventHandlers;
// using Sample.DeliveryService.API.Dtos.IncomingPayment;
// using Microsoft.Extensions.DependencyInjection;
// using Microsoft.Extensions.Hosting;
// using Lycia.Messaging.Abstractions;
// using Lycia.Saga; // For AddDefaultSagaIdGenerator
