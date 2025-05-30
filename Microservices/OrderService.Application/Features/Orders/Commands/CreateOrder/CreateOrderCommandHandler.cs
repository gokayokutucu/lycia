using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using OrderService.Application.Contracts.Persistence;
using OrderService.Domain.Aggregates.Order; // For Order and OrderItem domain entities
using OrderService.Domain.Events;          // For OrderCreatedDomainEvent

namespace OrderService.Application.Features.Orders.Commands.CreateOrder
{
    public class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, Guid>
    {
        private readonly IOrderRepository _orderRepository;
        private readonly IPublisher _publisher;

        public CreateOrderCommandHandler(IOrderRepository orderRepository, IPublisher publisher)
        {
            _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
            _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        }

        public async Task<Guid> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            if (request.Items == null || !request.Items.Any())
            {
                // Or handle this with a FluentValidation validator
                throw new ArgumentException("Order items cannot be empty.", nameof(request.Items));
            }

            // Map DTOs to Domain Entities for OrderItems
            var domainOrderItems = request.Items.Select(dto => new Domain.Aggregates.Order.OrderItem(
                dto.ProductId,
                dto.ProductName, // Assuming ProductName in DTO is directly used or enriched if null/empty
                dto.UnitPrice,
                dto.Quantity
            )).ToList();

            // Create the Order aggregate
            var order = new Domain.Aggregates.Order.Order(
                Guid.NewGuid(), // Generate new Order ID
                request.UserId,
                domainOrderItems
            );

            // Persist the order
            await _orderRepository.AddAsync(order, cancellationToken);

            // Create the domain event
            var domainEvent = new OrderCreatedDomainEvent(
                order.Id,
                order.UserId,
                order.CreatedDate
            );

            // Publish the domain event
            await _publisher.Publish(domainEvent, cancellationToken);

            return order.Id;
        }
    }
}
