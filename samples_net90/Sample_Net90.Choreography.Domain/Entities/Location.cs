using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sample_Net90.Choreography.Domain.Entities;

public sealed class StockLocation : BaseEntity
{
    public string Warehouse { get; set; }
    public string Zone { get; set; }
    public string Block { get; set; }
    public int Shelve { get; set; }
}
