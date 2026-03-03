using Ocelot.DependencyInjection;
using Ocelot.Middleware;
using Ocelot.Provider.Polly;
using Gateway.Api.Handlers;
using Gateway.Api.Middleware;
using MMLib.SwaggerForOcelot.DependencyInjection;
using Microsoft.OpenApi;
using Serilog;

// Configure Serilog with enrichers
Log.Logger = new LoggerConfiguration()
    .Enrich.WithProperty("Service", "Gateway.Api")
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .Enrich.WithThreadId()
    .Enrich.WithProcessId()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{Service}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.Seq("http://localhost:5341")
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("System", Serilog.Events.LogEventLevel.Warning)
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog();

builder.Configuration
    .AddJsonFile("ocelot.json", optional: false, reloadOnChange: true)
    .AddJsonFile("swagger-ocelot.json", optional: false, reloadOnChange: true);

builder.Services.AddSingleton<GatewayAuthenticationHandler>();

// Swagger için gerekli servisleri ekle
builder.Services.AddEndpointsApiExplorer();

// SwaggerForOcelot'tan önce temel Swagger servislerini kaydet
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Gateway API",
        Version = "v1"
    });
});

// Ocelot için Swagger entegrasyonu
builder.Services.AddSwaggerForOcelot(builder.Configuration);

builder.Services
    .AddOcelot(builder.Configuration)
    .AddPolly()
    .AddDelegatingHandler<GatewayAuthenticationHandler>(global: true);

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

app.UseStaticFiles();

app.UseSwaggerForOcelotUI(opt =>
{
    opt.PathToSwaggerGenerator = "/swagger/docs";
});

app.UseCors("AllowAll");

// Request/Response Logging Middleware - BEFORE Ocelot
app.UseMiddleware<RequestResponseLoggingMiddleware>();

await app.UseOcelot();

app.Run();