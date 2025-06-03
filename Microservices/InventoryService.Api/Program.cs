using InventoryService.Api.Controllers;
using System.Reflection;
var builder = WebApplication.CreateBuilder(args);

// 1) Register MediatR v12 handlers by specifying which assembly(ies) to scan:
builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssemblies(Assembly.GetExecutingAssembly())
);

builder.AddServiceDefaults();

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapInventoryEndpoints();

//app.MapInventoryEndpoints();

app.Run();
