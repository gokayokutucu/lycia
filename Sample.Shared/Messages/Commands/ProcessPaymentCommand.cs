using System;
using Lycia.Messaging;

namespace Sample.Shared.Messages.Commands
{
    public class ProcessPaymentCommand : CommandBase
    {
        public Guid OrderId { get; set; }
        public decimal Amount { get; set; }
        public string CardDetails { get; set; } // Placeholder
    }
}
