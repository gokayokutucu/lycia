using Dapr;
using Lycia.Dapr;
using Lycia.Dapr.Enums;
using Lycia.Dapr.Messages;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

//builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
//Add dapr
builder.Services.AddDaprClient();

var app = builder.Build();

// Dapr will send serialized event object vs. being raw CloudEvent
app.UseCloudEvents();
// needed for Dapr pub/sub routing
app.MapSubscribeHandler();

const string PUBSUB_NAME = "pubsub";
const string TOPIC_NAME = "OrderCreatedCommand";

app.MapPost("/orders", [Topic(PUBSUB_NAME, TOPIC_NAME)] (OrderCreated @event) =>
{
    Console.WriteLine("Subscriber received : " + @event);
    return Results.Ok(@event);
});

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

//app.UseAuthorization();

//app.MapControllers();

app.Run();