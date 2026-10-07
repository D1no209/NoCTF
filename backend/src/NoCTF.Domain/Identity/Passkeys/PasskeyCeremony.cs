using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NoCTF.Domain.Shared;

namespace NoCTF.Domain.Identity.Passkeys;

public enum PasskeyCeremonyPurpose : short { Registration, Login }
public enum PasskeyCeremonyState : short { Pending, Completed, Failed, Cancelled }

[Table("passkey_ceremonies")]
public sealed class PasskeyCeremony : IConcurrencyTracked
{
    [Key] public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    [ForeignKey(nameof(UserId))] public User? User { get; set; }
    public int? TokenVersion { get; set; }
    public bool LocalProofSatisfied { get; set; }
    public Guid MfaPolicyStamp { get; set; }
    public PasskeyCeremonyPurpose Purpose { get; set; }
    public PasskeyCeremonyState State { get; set; }
    [Required, MaxLength(64)] public string BrowserBindingHash { get; set; } = string.Empty;
    [Required, MaxLength(64)] public string ConfigurationFingerprint { get; set; } = string.Empty;
    [Required, MaxLength(256)] public string Origin { get; set; } = string.Empty;
    // State is created by the official WebAuthn handler and never accepted from a client.
    [Required, MaxLength(16384)] public byte[] ProtectedProtocolState { get; set; } = [];
    [MaxLength(64)] public string? Name { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    [MaxLength(2048)] public string ReturnPath { get; set; } = "/";
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}
