using System.Diagnostics;
using Lycia.Dapr.Extensions;
using Lycia.Dapr.Messages;
using Lycia.Dapr.Messages.Abstractions;
using Sample.Consumer.Handlers;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

//builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddScoped<OrderCreatedEventHandler>();
builder.Services.AddSingleton<IEventBus, DaprEventBus>();

//Add dapr
builder.Services
    .AddDaprClient();

var app = builder.Build();

#if DEBUG
Debugger.Launch();
#endif

// Dapr will send serialized event object vs. being raw CloudEvent
app.UseCloudEvents();


// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

//app.MapControllers();
// needed for Dapr pub/sub routing
app.MapSubscribeHandler();
app.MapDaprEventBus();

app.Run();