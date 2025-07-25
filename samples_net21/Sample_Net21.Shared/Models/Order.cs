using System;
using System.Collections.Generic;
using System.Text;

namespace Sample_Net21.Shared.Models
{
    public class Order : BaseModel
    {
        public Guid CustomerId { get; set; }
        public IEnumerable<Product> Products { get; set; } = new List<Product>();
    }
}
