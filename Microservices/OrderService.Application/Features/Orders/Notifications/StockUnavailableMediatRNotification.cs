using MediatR;
using Sample.Shared.Messages.Events; // For StockUnavailableEvent

namespace OrderService.Application.Features.Orders.Notifications
{
    public class StockUnavailableMediatRNotification : INotification
    {
        public StockUnavailableEvent OriginalEvent { get; }
        public StockUnavailableMediatRNotification(StockUnavailableEvent originalEvent) => OriginalEvent = originalEvent;
    }
}
