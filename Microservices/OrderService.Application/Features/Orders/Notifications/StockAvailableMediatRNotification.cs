using MediatR;
using Sample.Shared.Messages.Events; // For StockAvailableEvent

namespace OrderService.Application.Features.Orders.Notifications
{
    public class StockAvailableMediatRNotification : INotification
    {
        public StockAvailableEvent OriginalEvent { get; }
        public StockAvailableMediatRNotification(StockAvailableEvent originalEvent) => OriginalEvent = originalEvent;
    }
}
