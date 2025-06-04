using MediatR;
using Sample.Shared.Messages.Events;

namespace OrderService.Application.Features.Orders.Notifications
{
    public class OrderCreationFailedMediatRNotification : INotification
    {
        public OrderCreationFailedEvent OriginalEvent { get; }
        public OrderCreationFailedMediatRNotification(OrderCreationFailedEvent originalEvent) => OriginalEvent = originalEvent;
    }
}
