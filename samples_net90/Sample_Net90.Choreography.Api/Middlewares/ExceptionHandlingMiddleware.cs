using FluentValidation;
using System.Net;
using System.Text.Json;

namespace Sample_Net90.Choreography.Api.Middlewares;
public class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task Invoke(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (ValidationException ex)
        {
            logger.LogWarning("Validation error: {Errors}", ex.Errors);

            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            context.Response.ContentType = "application/json";

            var errors = ex.Errors.Select(e => new { e.PropertyName, e.ErrorMessage });
            var result = JsonSerializer.Serialize(new { errors });

            await context.Response.WriteAsync(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Unauthorized access on {Path}", context.Request.Path);

            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            context.Response.ContentType = "application/json";

            var result = JsonSerializer.Serialize(new { error = "Unauthorized" });

            await context.Response.WriteAsync(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception on {Path}", context.Request.Path);

            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            context.Response.ContentType = "application/json";

            var result = JsonSerializer.Serialize(new { error = "An unexpected error occurred." });

            await context.Response.WriteAsync(result);
        }
    }
}
