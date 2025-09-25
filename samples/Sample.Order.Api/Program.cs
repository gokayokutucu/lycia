// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Extensions;
using Lycia.Abstractions;
using Lycia.Extensions;
using Microsoft.AspNetCore.Mvc;
using Sample.Shared.Messages.Commands;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services
    .AddLycia(builder.Configuration)
    .AddSagasFromCurrentAssembly()
    .Build();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();


app.MapPost("/order", async (
        [FromBody] CreateOrderCommand command,
        [FromServices] IEventBus eventBus) =>
    {
        await eventBus.Send(command); 
        return Results.Accepted();
    })
    .WithName("CreateOrder");

await app.RunAsync();