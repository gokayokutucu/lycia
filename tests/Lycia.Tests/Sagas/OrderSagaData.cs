using Lycia.Saga;

namespace Lycia.Tests.Sagas;

/// <summary>
/// Saga data for the order creation process.
/// Carries shared state across multiple steps of the order saga.
/// </summary>
public class OrderSagaData
{
    public Guid OrderId { get; set; }
    public Guid UserId { get; set; }
    public decimal TotalPrice { get; set; }
}