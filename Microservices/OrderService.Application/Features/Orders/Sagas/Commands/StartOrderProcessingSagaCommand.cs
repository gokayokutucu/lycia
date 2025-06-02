using Lycia.Messaging; // For CommandBase

namespace OrderService.Application.Features.Orders.Sagas.Commands
{
    public class OrderItemSagaDto
    {
        public Guid ProductId { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
    }

    public class StartOrderProcessingSagaCommand : CommandBase // Inherits from Lycia.Messaging
    {
        public Guid OrderId { get; set; }
        public Guid UserId { get; set; }
        public decimal TotalPrice { get; set; }
        public List<OrderItemSagaDto> Items { get; set; } = new List<OrderItemSagaDto>();

        public StartOrderProcessingSagaCommand(Guid orderId, Guid userId, decimal totalPrice, List<OrderItemSagaDto> items)
        {
            // CommandId is typically set by CommandBase or Message base class if not overridden
            SagaId = orderId; // Often, the first command in a saga uses the aggregate ID as SagaId
            OrderId = orderId;
            UserId = userId;
            TotalPrice = totalPrice;
            Items = items ?? new List<OrderItemSagaDto>();
        }

        // Parameterless constructor for deserialization if needed
        public StartOrderProcessingSagaCommand() { }
    }
}
