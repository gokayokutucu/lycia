using System;
using Lycia.Saga;

namespace Sample.Shared.Messages.Sagas
{
    public class OrderStockCheckSagaData : SagaData
    {
        public Guid OrderId { get; set; }
        public Guid UserId { get; set; }
        public string ProductId { get; set; }
        public int Quantity { get; set; }
        public decimal TotalPrice { get; set; }
    }
}
