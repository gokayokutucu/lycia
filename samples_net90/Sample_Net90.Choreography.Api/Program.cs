using Lycia.Extensions;
using Lycia.Saga.Extensions;
using Sample_Net90.Choreography.Api.EndPoints;
using Sample_Net90.Choreography.Api.Middleware;

namespace Sample_Net90.Choreography.Api;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddAuthorization();

        builder.Services.AddOpenApi();

        builder.Services
            .AddLycia(builder.Configuration)
            .AddSagasFromCurrentAssembly();

        var app = builder.Build();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        app.UseHttpsRedirection();

        app.UseAuthorization();

        app.UseMiddleware<RequestResponseLoggingMiddleware>();
        app.UseMiddleware<ExceptionHandlingMiddleware>();

        app.MapOrdersEndpoints();

        app.Run();
    }
}
