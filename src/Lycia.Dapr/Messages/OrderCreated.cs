using Lycia.Dapr.Enums;
using Lycia.Dapr.Messages;
using System.Text.Json.Serialization;

namespace Lycia.Dapr;

public class OrderCreated : Event
{
    public OrderCreated(OrderStatus orderStatus)
    {
        Status = orderStatus;
    }

    public OrderCreated()
    {
    }

    public OrderStatus Status { get; }

    public int Id { get; protected set; } = Random.Shared.Next(1, 10);
    public Guid OrderId { get; protected set; } = Guid.NewGuid();
}