using Lycia.Infrastructure.Abstractions;
using Lycia.Infrastructure.Dispatching;
using Lycia.Infrastructure.Eventing;
using Lycia.Infrastructure.Stores;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Extensions;
using Lycia.Saga.Handlers;
using Microsoft.AspNetCore.Mvc;
using Sample.Shared.Messages.Commands;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddScoped<ISagaIdGenerator, DefaultSagaIdGenerator>();
builder.Services.AddScoped<IEventBus>(sp =>
    new InMemoryEventBus(new Lazy<ISagaDispatcher>(sp.GetRequiredService<ISagaDispatcher>)));
builder.Services.AddScoped<ISagaStore, InMemorySagaStore>();
builder.Services.AddScoped<ISagaDispatcher, SagaDispatcher>();

builder.Services.Scan(scan => scan
    .FromApplicationDependencies()
    .AddClasses(classes => classes.AssignableTo(typeof(ReactiveSagaHandler<>)))
    .AsSelfWithInterfaces()
    .WithScopedLifetime()
    .AddClasses(classes => classes.AssignableTo(typeof(CoordinatedSagaHandler<,,>)))
    .AsSelfWithInterfaces()
    .WithScopedLifetime()
    .AddClasses(classes => classes.AssignableTo(typeof(ResponseSagaHandler<,>)))
    .AsSelfWithInterfaces()
    .WithScopedLifetime()
    .AddClasses(classes => classes.AssignableTo(typeof(ISagaHandlerWithContext<>)))
    .AsImplementedInterfaces()
    .WithScopedLifetime()
    .AddClasses(classes => classes.AssignableTo(typeof(ISagaHandlerWithContext<,>)))
    .AsImplementedInterfaces()
    .WithScopedLifetime()
);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();


app.MapPost("/order", async (
        [FromBody] CreateOrderCommand command,
        [FromServices] ISagaDispatcher dispatcher) =>
    {
        await dispatcher.DispatchAsync(command);
        return Results.Accepted();
    })
    .WithName("CreateOrder");

app.Run();