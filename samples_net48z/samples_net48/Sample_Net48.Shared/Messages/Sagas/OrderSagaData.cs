using Lycia.Saga;
using System;

namespace Sample_Net48.Shared.Messages.Sagas
{
    /// <summary>
    /// Saga data for the order creation process.
    /// Carries shared state across multiple steps of the order saga.
    /// </summary>
    public class OrderSagaData : SagaData
    {
        public Guid OrderId { get; set; }
        public Guid UserId { get; set; }
        public decimal TotalPrice { get; set; }
    }
}