using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NoCTF.Domain.Shared;

namespace NoCTF.Domain.Identity.Mfa;

public enum MfaChallengePurpose : short { Login, Enrollment, Rebind, RecoveryGrant, RecoveryEnrollment, StepUp }
public enum MfaChallengeState : short { Pending, Completed, Cancelled, Failed, Verified }
public enum MfaOperation : short { EnableTotp, RebindTotp, RegenerateRecoveryCodes, DisableTotp, ChangePolicy, ChangeOidcTrust, ChangeAccountRequirement, GrantRecovery }
public enum MfaMailState : short { None, Pending, Sent }

[Table("mfa_challenges")]
public sealed class MfaChallenge : IConcurrencyTracked
{
    [Key] public Guid Id { get; set; }
    public Guid UserId { get; set; }
    [ForeignKey(nameof(UserId))] public User User { get; set; } = null!;
    public MfaChallengePurpose Purpose { get; set; }
    public MfaChallengeState State { get; set; }
    public MfaOperation? Operation { get; set; }
    public Guid? TargetUserId { get; set; }
    public Guid? TargetResourceId { get; set; }
    public Guid? ActorUserId { get; set; }
    [MaxLength(64)] public string? BrowserBindingHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public int FailedAttempts { get; set; }
    public int TokenVersion { get; set; }
    public Guid PolicyStamp { get; set; }
    public Guid? CredentialId { get; set; }
    public Guid? PendingCredentialId { get; set; }
    [MaxLength(256)] public byte[]? PendingSecretCiphertext { get; set; }
    public AuthenticationMethod PrimaryMethod { get; set; }
    public DateTimeOffset PrimaryAuthenticatedAt { get; set; }
    public Guid? PrimaryProviderId { get; set; }
    [MaxLength(128)] public string? PrimaryProviderFingerprint { get; set; }
    [Length(32, 32)] public byte[]? RecoveryGrantSha256 { get; set; }
    [MaxLength(256)] public byte[]? RecoveryGrantCiphertext { get; set; }
    public MfaMailState MailState { get; set; }
    [MaxLength(2048)] public string ReturnPath { get; set; } = "/";
    [MaxLength(1024)] public string? Reason { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}
