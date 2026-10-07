using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NoCTF.Domain.Shared;

namespace NoCTF.Domain.Identity.Passkeys;

public enum PasskeyTransport : short { Usb, Nfc, Ble, SmartCard, Hybrid, Internal }

[Table("user_passkeys")]
public sealed class UserPasskey : IConcurrencyTracked
{
    [Key] public Guid Id { get; set; }
    public Guid UserId { get; set; }
    [ForeignKey(nameof(UserId))] public User User { get; set; } = null!;
    [Required, MaxLength(1023)] public byte[] CredentialId { get; set; } = [];
    [Required, MaxLength(4096)] public byte[] PublicKey { get; set; } = [];
    [Required, MaxLength(64)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(253)] public string RelyingPartyId { get; set; } = string.Empty;
    public uint SignCount { get; set; }
    public bool IsUserVerified { get; set; }
    public bool IsBackupEligible { get; set; }
    public bool IsBackedUp { get; set; }
    // Immutable protocol evidence, not a serialized business aggregate.
    [Required, MaxLength(16384)] public byte[] AttestationObject { get; set; } = [];
    [Required, MaxLength(4096)] public byte[] ClientDataJson { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public List<UserPasskeyTransport> Transports { get; set; } = [];
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

[Table("user_passkey_transports")]
public sealed class UserPasskeyTransport
{
    public Guid UserPasskeyId { get; set; }
    public int Position { get; set; }
    public PasskeyTransport Transport { get; set; }
}
