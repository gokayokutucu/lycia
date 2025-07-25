using Lycia.Messaging;


namespace Sample_Net21.Shared.Messages.Events
{
    public sealed class StockReservationFailedEvent : EventBase
    {
        public int StockId { get; set; }
    }
}