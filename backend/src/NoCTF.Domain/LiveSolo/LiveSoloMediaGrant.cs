using System.ComponentModel.DataAnnotations;
using NoCTF.Domain.Identity.Mfa;
using NoCTF.Domain.Shared;

namespace NoCTF.Domain.LiveSolo;

public enum LiveSoloMediaGrantRole : short { Publisher, Judge, Director }

public sealed class LiveSoloMediaGrant : IConcurrencyTracked
{
    public Guid Id { get; set; }
    public Guid MediaSessionId { get; set; }
    public Guid UserId { get; set; }
    public LiveSoloMediaGrantRole Role { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    [MaxLength(128)] public string Identity { get; set; } = "";
    public int TokenVersion { get; set; }
    public AuthenticationMethod AuthenticationMethod { get; set; }
    public DateTimeOffset AuthenticatedAt { get; set; }
    public MfaSource MfaSource { get; set; }
    public DateTimeOffset? MfaAuthenticatedAt { get; set; }
    public Guid? MfaCredentialId { get; set; }
    public Guid? ProviderId { get; set; }
    public Guid? TrustPolicyId { get; set; }
    public Guid? PrimaryCredentialId { get; set; }
    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}
