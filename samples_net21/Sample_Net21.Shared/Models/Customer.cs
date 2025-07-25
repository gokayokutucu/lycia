using System;
using System.Collections.Generic;
using System.Text;

namespace Sample_Net21.Shared.Models
{
    public sealed class Customer : BaseModel
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public IEnumerable<Address> Addresses { get; set; } = new List<Address>();
        public IEnumerable<Card> Cards { get; set; } = new List<Card>();
    }
}
