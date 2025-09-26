using Autofac.Core;
using Lycia.Extensions;
using Lycia.Extensions.Logging;
using Lycia.Saga.Extensions;
using Microsoft.EntityFrameworkCore;
using Sample_Net90.Choreography.Api.EndPoints;
using Sample_Net90.Choreography.Api.Middlewares;
using Sample_Net90.Choreography.Application;
using Sample_Net90.Choreography.Infrastructure;
using Sample_Net90.Choreography.Infrastructure.Persistence;
using System.Configuration;

namespace Sample_Net90.Choreography.Api;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddAuthorization();

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        builder.Services.AddInfrastructureServices(builder.Configuration);
        builder.Services.AddApplicationServices(builder.Configuration);
        builder.Services
            .AddLycia(builder.Configuration)
            .UseSagaMiddleware(opt =>
            {
                opt.AddMiddleware<SerilogLoggingMiddleware>();
                //opt.AddMiddleware<RetryMiddleware>();
            })
            .AddSagasFromCurrentAssembly()
            .Build();


        var app = builder.Build();

        using (var scope = app.Services.CreateScope()) // <- after app is built
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            dbContext.Database.Migrate();
            //dbContext.CreateAuditInfrastructure();
        }


        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "Sample .Net9 Choreography API V1");
                c.RoutePrefix = string.Empty;
            });
        }

        app.UseHttpsRedirection();

        app.UseAuthorization();

        app.UseMiddleware<RequestResponseLoggingMiddleware>();
        app.UseMiddleware<ExceptionHandlingMiddleware>();

        app.MapOrdersEndpoints();

        app.Run();
    }
}
