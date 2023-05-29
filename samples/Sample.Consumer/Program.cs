using Lycia.Dapr.EventBus;
using Lycia.Dapr.EventBus.Abstractions;
using Lycia.Dapr.Extensions;
using Lycia.Dapr.Messages;
using Lycia.Dapr.Messages.Abstractions;
using Sample.Consumer.Handlers;
using Sample.Domain.Messages;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddScoped<OrderCreatedEventHandler>();
builder.Services.AddSingleton<IEventBus, DaprEventBus>();

//Add dapr
builder.Services.AddDaprClient();

var app = builder.Build();

// Dapr will send serialized event object vs. being raw CloudEvent
app.UseCloudEvents();
// needed for Dapr pub/sub routing
app.MapSubscribeHandler();
app.MapDaprEventBus(eventBus =>
{
    // Subscribe with a handler
    eventBus.Subscribe(typeof(OrderCreatedEventHandler).Assembly, "", "v1");
});

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapControllers();

app.Run();