namespace NoCTF.Domain.Shared;

/// <summary>Marks a mutable aggregate protected by provider-neutral optimistic concurrency.</summary>
public interface IConcurrencyTracked
{
    Guid ConcurrencyStamp { get; set; }
}
