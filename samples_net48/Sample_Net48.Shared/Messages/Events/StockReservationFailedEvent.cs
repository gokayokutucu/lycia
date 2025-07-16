using Lycia.Messaging;


namespace Sample_Net48.Shared.Messages.Events
{
    public sealed class StockReservationFailedEvent : EventBase
    {
        public int StockId { get; set; }
    }
}