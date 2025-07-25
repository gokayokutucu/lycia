using System;
using System.Collections.Generic;
using System.Text;

namespace Sample_Net21.Shared.Models
{
    public class Product : BaseModel
    {
        public string Name { get; set; }
        public decimal Price { get; set; }
        public CurrencyType Currency { get; set; }
    }
    public enum CurrencyType
    {
        USD,
        EUR,
        GBP,
        TRY,
    }
}
