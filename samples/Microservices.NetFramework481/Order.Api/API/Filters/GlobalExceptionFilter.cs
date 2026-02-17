using System;
using System.Net;
using System.Net.Http;
using System.Web.Http.Filters;
using Serilog;

namespace Sample.Order.NetFramework481.API.Filters;

/// <summary>
/// Global exception filter for Web API.
/// </summary>
public sealed class GlobalExceptionFilter : ExceptionFilterAttribute
{
    /// <summary>
    /// Handles exceptions.
    /// </summary>
    public override void OnException(HttpActionExecutedContext context)
    {
        Log.Error(context.Exception, "Unhandled exception in {ControllerName}.{ActionName}",
            context.ActionContext.ControllerContext.ControllerDescriptor.ControllerName,
            context.ActionContext.ActionDescriptor.ActionName);

        context.Response = context.Request.CreateResponse(
            HttpStatusCode.InternalServerError,
            new
            {
                StatusCode = 500,
                Message = "An error occurred while processing your request",
                Detailed = context.Exception.Message
            });
    }
}
