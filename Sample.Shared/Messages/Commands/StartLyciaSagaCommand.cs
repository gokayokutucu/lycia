using System;
using System.Collections.Generic;
using Lycia.Messaging;

namespace Sample.Shared.Messages.Commands
{
    public class StartLyciaSagaCommand : CommandBase
    {
        public Guid OrderId { get; set; }
        public Guid UserId { get; set; }
        public decimal TotalPrice { get; set; }
        public List<OrderItem> Items { get; set; }
        public string CardDetails { get; set; }
        public string ShippingAddress { get; set; }
        public string UserEmail { get; set; }
    }
}
