using System;
using System.Collections.Generic;
using Lycia.Messaging;

namespace Sample.Shared.Messages.Commands
{
    public class UpdateInventoryCommand : CommandBase
    {
        public Guid OrderId { get; set; }
        public List<OrderItem> Items { get; set; }
    }
}
