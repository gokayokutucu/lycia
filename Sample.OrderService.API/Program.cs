var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Register custom services
// Removed: builder.Services.AddSingleton<Sample.OrderService.API.Interfaces.IMessagePublisher, Sample.OrderService.API.Services.ConsoleMessagePublisher>();
builder.Services.AddScoped<Sample.OrderService.API.Services.OrderCreationService>();

// Add Lycia RabbitMQ Publisher
// This uses the extension method from Lycia.Infrastructure
// It will use the default connection string "amqp://guest:guest@localhost:5672"
// if no URI is provided and IConfiguration isn't set up to provide one.
builder.Services.AddRabbitMqPublisher(); // Registers IMessagePublisher, IRabbitMqChannelProvider

// Add Lycia Saga Core Services
builder.Services.AddDefaultSagaIdGenerator(); // From Lycia.Saga - Registers ISagaIdGenerator

// Add Lycia Infrastructure Services for Saga
builder.Services.AddRabbitMqEventBus(); // From Lycia.Infrastructure - Registers IEventBus (needed by RedisSagaStore context)
builder.Services.AddRedisSagaStore(builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379"); // Registers ISagaStore

// Register Event Handlers for compensation
builder.Services.AddScoped<Lycia.Messaging.Abstractions.IEventHandler<Sample.OrderService.API.Dtos.IncomingInventory.StockReleasedEventDto>, Sample.OrderService.API.EventHandlers.StockReleasedEventHandler>();
builder.Services.AddScoped<Lycia.Messaging.Abstractions.IEventHandler<Sample.OrderService.API.Dtos.IncomingPayment.PaymentFailedEventDto>, Sample.OrderService.API.EventHandlers.PaymentFailedEventHandler>();
builder.Services.AddScoped<Lycia.Messaging.Abstractions.IEventHandler<Sample.OrderService.API.Dtos.IncomingInventory.StockReservationFailedEventDto>, Sample.OrderService.API.EventHandlers.StockReservationFailedEventHandler>();


var app = builder.Build();

// Configure and start RabbitMQ subscriber if IMessageSubscriber is registered
if (app.Services.GetService<Lycia.Messaging.Abstractions.IMessageSubscriber>() != null)
{
    try
    {
        var subscriber = app.Services.GetRequiredService<Lycia.Messaging.Abstractions.IMessageSubscriber>();
        
        // Compensation event subscriptions
        subscriber.Subscribe<Sample.OrderService.API.Dtos.IncomingInventory.StockReleasedEventDto>(
            "order_service_saga_events_q", 
            "saga_events_exchange", 
            "order.stock.released");

        subscriber.Subscribe<Sample.OrderService.API.Dtos.IncomingPayment.PaymentFailedEventDto>(
            "order_service_saga_events_q", 
            "saga_events_exchange", 
            "order.payment.failed");

        subscriber.Subscribe<Sample.OrderService.API.Dtos.IncomingInventory.StockReservationFailedEventDto>(
            "order_service_saga_events_q", 
            "saga_events_exchange", 
            "order.stock.reservation_failed");
        
        subscriber.StartListening();

        // Ensure subscriber is disposed on application shutdown
        app.Lifetime.ApplicationStopping.Register(() => {
            var sub = app.Services.GetRequiredService<Lycia.Messaging.Abstractions.IMessageSubscriber>();
            sub.Dispose(); 
        });
        app.Logger.LogInformation("RabbitMQ IMessageSubscriber started and configured for OrderService compensation events.");
    }
    catch (Exception ex)
    {
        app.Logger.LogCritical(ex, "Failed to start or configure RabbitMQ IMessageSubscriber for OrderService.");
        // Depending on policy, might rethrow or exit if subscriber is critical
    }
}


// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    // Ensure OpenAPI UI is available for testing the endpoint.
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();

// Required for Lycia.Infrastructure.ServiceCollectionExtensions
// if not already implicitly available through other usings or SDK.
// using Lycia.Infrastructure; 
// using Lycia.Saga; // For AddDefaultSagaIdGenerator
// using Sample.OrderService.API.EventHandlers; // For the new handlers
// using Sample.OrderService.API.Dtos.IncomingInventory; // For DTOs
// using Sample.OrderService.API.Dtos.IncomingPayment; // For DTOs
// using Lycia.Messaging.Abstractions; // For IMessageSubscriber, IEventHandler
// using Microsoft.Extensions.DependencyInjection; // For GetRequiredService
// using Microsoft.Extensions.Hosting; // For ApplicationStopping
