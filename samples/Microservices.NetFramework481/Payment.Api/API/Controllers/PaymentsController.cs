using System;
using System.Threading.Tasks;
using System.Web.Http;
using MediatR;
using Sample.Payment.NetFramework481.Application.Payments.UseCases.Commands.Process;
using Sample.Payment.NetFramework481.Domain.Payments;
using Sample.Payment.NetFramework481.API.Filters;

namespace Sample.Payment.NetFramework481.API.Controllers;

/// <summary>
/// Payments API controller.
/// </summary>
[RoutePrefix("api/payments")]
[GatewayOnlyFilter]
public sealed class PaymentsController(IMediator mediator) : ApiController
{
    /// <summary>
    /// Process a payment.
    /// </summary>
    [HttpPost]
    [Route("process")]
    public async Task<IHttpActionResult> ProcessPayment([FromBody] Guid orderId)
    {
        var command = ProcessPaymentCommand.Create(orderId);

        var result = await mediator.Send(command);

        if (result.Status == PaymentStatus.Failed)
            return BadRequest(result.FailureReason);

        return Ok(result);
    }
}
