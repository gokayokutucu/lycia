using MediatR;
using Sample.Shared.Messages.Events; // For OrderCreationInitiatedEvent

namespace InventoryService.Application.Features.Stocks.Notifications
{
    public class OrderCreationInitiatedMediatRNotification : INotification
    {
        public OrderCreationInitiatedEvent OriginalEvent { get; }
        public OrderCreationInitiatedMediatRNotification(OrderCreationInitiatedEvent originalEvent) => OriginalEvent = originalEvent;
    }
}
