using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sample_Net90.Choreography.Domain.Entities;

public sealed class Customer : BaseEntity
{
    public string FirstName { get; init; }
    public string LastName { get; init; }
    public string Email { get; init; }
    public string PhoneNumber { get; init; }
    public DateTime DateOfBirth { get; init; }

    public IEnumerable<Guid> Addresses { get; init; }
    public IEnumerable<Guid> Cards { get; init; }
    public IEnumerable<Guid> Orders { get; init; }
}
