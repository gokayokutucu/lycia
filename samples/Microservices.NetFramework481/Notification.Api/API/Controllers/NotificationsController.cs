using MediatR;
using Sample.Notification.NetFramework481.API.Filters;
using System.Web.Http;

namespace Sample.Notification.NetFramework481.API.Controllers;

/// <summary>
/// Notifications API controller.
/// </summary>
[RoutePrefix("api/notifications")]
[GatewayOnlyFilter]
public sealed class NotificationsController(IMediator mediator) : ApiController
{

}
