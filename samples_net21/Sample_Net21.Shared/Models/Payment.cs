using System;
using System.Collections.Generic;
using System.Text;

namespace Sample_Net21.Shared.Models
{
    public class Payment : BaseModel
    {
        public Guid OrderId { get; set; }
        public Guid CardId { get; set; }
    }
}
