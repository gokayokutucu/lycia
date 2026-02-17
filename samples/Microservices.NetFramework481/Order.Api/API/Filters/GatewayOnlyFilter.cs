using Shared.Contracts.Constants;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http.Controllers;
using System.Web.Http.Filters;

namespace Sample.Order.NetFramework481.API.Filters;

/// <summary>
/// Action filter that ensures requests are coming from the API Gateway only.
/// Returns 403 Forbidden if the internal gateway header is missing or invalid.
/// </summary>
public sealed class GatewayOnlyFilter : ActionFilterAttribute
{
    public override void OnActionExecuting(HttpActionContext actionContext)
    {
        // Check for gateway authentication header
        if (!actionContext.Request.Headers.TryGetValues(GatewayAuth.HeaderName, out var headerValues))
        {
            actionContext.Response = actionContext.Request.CreateResponse(
                HttpStatusCode.Forbidden,
                new { error = "Access denied. Requests must come through API Gateway." });
            return;
        }

        var providedKey = headerValues.FirstOrDefault();
        if (providedKey != GatewayAuth.SecretKey)
        {
            actionContext.Response = actionContext.Request.CreateResponse(
                HttpStatusCode.Forbidden,
                new { error = "Invalid gateway authentication." });
            return;
        }

        base.OnActionExecuting(actionContext);
    }
}
