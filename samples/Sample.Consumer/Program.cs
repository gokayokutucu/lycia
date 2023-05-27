using Dapr;
using Lycia.Dapr;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddDaprClient();

var app = builder.Build();

// Dapr will send serialized event object vs. being raw CloudEvent
app.UseCloudEvents();

// needed for Dapr pub/sub routing
app.MapSubscribeHandler();

const string PUBSUB_NAME = "pubsub";
const string TOPIC_NAME = "orders";

app.MapPost("/orders", [Topic(PUBSUB_NAME, TOPIC_NAME)] (Product product) =>
{
    Console.WriteLine("Subscriber received : " + product.Name);
    return Results.Ok(product);
});

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.Run();