using Lycia.Dapr.Enums;
using Lycia.Dapr.Messages;

namespace Lycia.Dapr;

public class OrderCreated : Event
{
    // public OrderCreated(OrderStatus orderStatus)
    // {
    //     Status = orderStatus;
    // }
    //    public OrderStatus Status { get; }
    
    public OrderCreated(string name)
    {
        Name = name;
    }

    public int Id { get; protected set; } = Random.Shared.Next(1, 10);
    public Guid OrderId { get; protected set; } = Guid.NewGuid();
    public string Name { get; set; }
}