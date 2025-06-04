using MediatR;
using OrderService.Application.Features.Orders.Notifications; // For MediatR Wrappers
using OrderService.Application.Contracts.Persistence;
using OrderService.Domain.Entities;
using System.Threading;
using System.Threading.Tasks;
using System;

namespace OrderService.Application.Features.Orders.Consumers
{
    public class OrderCreationFailedConsumer : INotificationHandler<OrderCreationFailedMediatRNotification>
    {
        private readonly IOrderRepository _orderRepository;

        public OrderCreationFailedConsumer(IOrderRepository orderRepository)
        {
            _orderRepository = orderRepository;
        }

        public async Task Handle(OrderCreationFailedMediatRNotification notification, CancellationToken cancellationToken)
        {
            var originalEvent = notification.OriginalEvent;
            Console.WriteLine($"Handling OrderCreationFailedEvent (via MediatR wrapper) for OrderId: {originalEvent.OrderId}. Reason: {originalEvent.Reason}");
            var order = await _orderRepository.GetByIdAsync(originalEvent.OrderId, cancellationToken);

            if (order != null)
            {
                // TODO: Update order status to 'Failed' or 'Cancelled'
                // var updatedOrder = order with { Status = "Failed", FailureReason = originalEvent.Reason };
                // await _orderRepository.UpdateAsync(updatedOrder, cancellationToken);

                Console.WriteLine($"Order {originalEvent.OrderId} found. In a real scenario, its status would be updated to Failed/Cancelled. Reason: {originalEvent.Reason}");
            }
            else
            {
                Console.WriteLine($"Order {originalEvent.OrderId} not found. Cannot update status to Failed/Cancelled.");
            }
        }
    }
}
