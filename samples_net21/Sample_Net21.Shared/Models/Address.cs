using System;
using System.Collections.Generic;
using System.Text;

namespace Sample_Net21.Shared.Models
{
    public class Address : BaseModel
    {
        public string Title { get; set; } = string.Empty;
        public int CountryId { get; set; }
        public int RegionId { get; set; }
        public int CityId { get; set; }
        public int StreetId { get; set; }
        public int BuildingId { get; set; }
        public int Floor { get; set; }
        public int Apartment { get; set; }
        public string ZipCode { get; set; } = string.Empty;
    }
}
