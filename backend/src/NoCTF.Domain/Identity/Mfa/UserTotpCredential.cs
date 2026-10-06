using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NoCTF.Domain.Shared;

namespace NoCTF.Domain.Identity.Mfa;

[Table("user_totp_credentials")]
public sealed class UserTotpCredential : IConcurrencyTracked
{
    [Key] public Guid Id { get; set; }
    public Guid UserId { get; set; }
    [ForeignKey(nameof(UserId))] public User User { get; set; } = null!;
    [Required, MaxLength(256)] public byte[] SecretCiphertext { get; set; } = [];
    public DateTimeOffset EnabledAt { get; set; }
    public long LastAcceptedStep { get; set; } = -1;
    public Guid RecoveryBatchId { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}
