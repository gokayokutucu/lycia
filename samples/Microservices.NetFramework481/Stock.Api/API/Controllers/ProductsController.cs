using System.Web.Http;
using Sample.Product.NetFramework481.API.Filters;

namespace Sample.Product.NetFramework481.API.Controllers;

/// <summary>
/// Products API controller.
/// </summary>
[RoutePrefix("api/products")]
[GatewayOnlyFilter]
public sealed class ProductsController : ApiController
{

}
