using Lycia.Dapr.Messages;

namespace Lycia.Dapr;

public class Product : Event
{
    public Product(string name)
    {
        Name = name;
    }

    public int Id { get; protected set; } = Random.Shared.Next(1, 10);
    public Guid OrderId { get; protected set; } = Guid.NewGuid();
    public string Name { get; set; }
}