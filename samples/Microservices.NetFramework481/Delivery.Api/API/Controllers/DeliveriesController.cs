                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                               using System.Web.Http;
using Sample.Delivery.NetFramework481.API.Filters;

namespace Sample.Delivery.NetFramework481.API.Controllers;

/// <summary>
/// Deliveries API controller.
/// </summary>
[RoutePrefix("api/deliveries")]
[GatewayOnlyFilter]
public sealed class DeliveriesController : ApiController
{

}
