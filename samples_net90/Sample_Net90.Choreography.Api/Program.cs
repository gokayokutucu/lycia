using Lycia.Extensions;
using Lycia.Saga.Extensions;
using Sample_Net90.Choreography.Api.EndPoints;
using Sample_Net90.Choreography.Api.Middlewares;
using Sample_Net90.Choreography.Application;

namespace Sample_Net90.Choreography.Api;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddAuthorization();

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        builder.Services
            .AddLycia(builder.Configuration)
            .AddSagasFromCurrentAssembly();

        builder.Services.AddApplicationServices();

        var app = builder.Build();

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
