using System;
using Lycia.Messaging;

namespace Sample.Shared.Messages.Commands
{
    public class SendNotificationCommand : CommandBase
    {
        public Guid OrderId { get; set; }
        public string Message { get; set; }
        public string UserEmail { get; set; }
    }
}
