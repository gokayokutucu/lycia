using MediatR;
using OrderService.Application.Features.Orders.Notifications; // For MediatR Wrappers
using OrderService.Application.Contracts.Persistence;
using OrderService.Domain.Entities;
using System.Threading;
using System.Threading.Tasks;
using System;

namespace OrderService.Application.Features.Orders.Consumers
{
    public class OrderConfirmedConsumer : INotificationHandler<OrderConfirmedMediatRNotification>
    {
        private readonly IOrderRepository _orderRepository;

        public OrderConfirmedConsumer(IOrderRepository orderRepository)
        {
            _orderRepository = orderRepository;
        }

        public async Task Handle(OrderConfirmedMediatRNotification notification, CancellationToken cancellationToken)
        {
            var originalEvent = notification.OriginalEvent;
            Console.WriteLine($"Handling OrderConfirmedEvent (via MediatR wrapper) for OrderId: {originalEvent.OrderId}");
            var order = await _orderRepository.GetByIdAsync(originalEvent.OrderId, cancellationToken);

            if (order != null)
            {
                // TODO: Update order status to 'Confirmed'
                // var updatedOrder = order with { Status = "Confirmed" };
                // await _orderRepository.UpdateAsync(updatedOrder, cancellationToken);

                Console.WriteLine($"Order {originalEvent.OrderId} found. In a real scenario, its status would be updated to Confirmed.");
            }
            else
            {
                Console.WriteLine($"Order {originalEvent.OrderId} not found. Cannot update status to Confirmed.");
            }
        }
    }
}
