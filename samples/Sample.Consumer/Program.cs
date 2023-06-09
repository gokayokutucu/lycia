using Lycia.Dapr.EventBus.Abstractions;
using Lycia.Dapr.EventBus.Sns;
using Sample.Consumer.Handlers;
using Sample.Domain.Enums;
using Sample.Domain.Messages;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddScoped<OrderCreatedEventHandler>();

builder.Services.AddAmazonSqs(builder.Configuration);
builder.Services.AddScoped<IEventPublisher, AmazonSnsEventPublisher>();

IServiceProvider serviceProvider = builder.Services.BuildServiceProvider();
using (IServiceScope scope = serviceProvider.CreateScope())
{
    IEventPublisher eventPublisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
    eventPublisher.PublishEventAsync(new OrderCreated(OrderStatus.Created),CancellationToken.None).Wait();
}

var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();
app.MapControllers();
app.Run();