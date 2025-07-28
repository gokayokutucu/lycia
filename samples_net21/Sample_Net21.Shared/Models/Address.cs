using System;
using System.Collections.Generic;
using System.Text;

namespace Sample_Net21.Shared.Models
{
    public class Address : BaseModel
    {
        public string Title { get; set; } = string.Empty;
        public string Country { get; set; }
        public string Region { get; set; }
        public string City { get; set; }
        public string Street { get; set; }
        public string Building { get; set; }
        public string Floor { get; set; }
        public string Apartment { get; set; }
        public string ZipCode { get; set; } = string.Empty;
    }
}
