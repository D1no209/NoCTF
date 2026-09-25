using System.ComponentModel.DataAnnotations;
using NoCTF.Domain.Shared;

namespace NoCTF.Domain.Runtime;

/// <summary>Single provider-neutral fencing row for capacity allocation transactions.</summary>
public sealed class RuntimeCapacityLedger : IConcurrencyTracked
{
    [Key]
    public short Id { get; set; } = 1;
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}
