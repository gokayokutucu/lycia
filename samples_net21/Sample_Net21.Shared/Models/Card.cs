using System;
using System.Collections.Generic;
using System.Text;

namespace Sample_Net21.Shared.Models
{
    public class Card : BaseModel
    {
        public string Title { get; set; } = string.Empty;
        public int BankId { get; set; }
        public string CardHolderName { get; set; } = string.Empty;
        public string CardNumber { get; set; } = string.Empty;
        public DateTime ExpiryDate { get; set; }
        public string CVV { get; set; } = string.Empty;
        public CardTypes Type { get; set; }
    }
    public enum CardTypes
    {
        Credit,
        Debit,
        Prepaid
    }
}
