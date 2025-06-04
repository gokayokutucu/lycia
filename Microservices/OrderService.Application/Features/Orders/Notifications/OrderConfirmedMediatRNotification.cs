using MediatR;
using Sample.Shared.Messages.Events;

namespace OrderService.Application.Features.Orders.Notifications
{
    public class OrderConfirmedMediatRNotification : INotification
    {
        public OrderConfirmedEvent OriginalEvent { get; }
        public OrderConfirmedMediatRNotification(OrderConfirmedEvent originalEvent) => OriginalEvent = originalEvent;
    }
}
