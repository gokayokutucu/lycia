using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Lycia.Saga.Abstractions; // For Lycia's IEventBus
using OrderService.Domain.Events; // For OrderCreatedDomainEvent
using OrderService.Application.Features.Orders.Sagas.Commands; // For StartOrderProcessingSagaCommand and OrderItemSagaDto

namespace OrderService.Application.Features.Orders.EventHandlers
{
    public class OrderCreatedSagaStartHandler : INotificationHandler<OrderCreatedDomainEvent>
    {
        private readonly IEventBus _lyciaEventBus;
        // No IOrderRepository needed here if OrderCreatedDomainEvent has all necessary data

        public OrderCreatedSagaStartHandler(IEventBus lyciaEventBus)
        {
            _lyciaEventBus = lyciaEventBus ?? throw new ArgumentNullException(nameof(lyciaEventBus));
        }

        public async Task Handle(OrderCreatedDomainEvent notification, CancellationToken cancellationToken)
        {
            if (notification == null)
            {
                throw new ArgumentNullException(nameof(notification));
            }

            // Map OrderItemDomainDto from the event to OrderItemSagaDto for the command
            var sagaOrderItems = notification.Items.Select(itemDto => new OrderItemSagaDto
            {
                ProductId = itemDto.ProductId,
                Quantity = itemDto.Quantity,
                UnitPrice = itemDto.UnitPrice
                // ProductName is not in OrderItemSagaDto currently, can be added if needed by saga
            }).ToList();

            var startSagaCommand = new StartOrderProcessingSagaCommand(
                notification.OrderId, // This will also be used as SagaId in the command's constructor
                notification.UserId,
                notification.TotalPrice,
                sagaOrderItems
            );

            // Dispatch the command to Lycia's event bus to initiate the saga
            await _lyciaEventBus.Send(startSagaCommand, cancellationToken);

            // Optionally, log that the saga start command has been dispatched
            Console.WriteLine($"Dispatched StartOrderProcessingSagaCommand for OrderId: {notification.OrderId}");
        }
    }
}
