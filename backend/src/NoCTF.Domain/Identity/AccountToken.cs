using System.ComponentModel.DataAnnotations;

namespace NoCTF.Domain.Identity;

public enum AccountTokenKind : short
{
    EmailVerification,
    PasswordReset
}

public sealed class AccountToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public AccountTokenKind Kind { get; set; }

    [Length(32, 32)]
    public byte[] TokenSha256 { get; set; } = [];

    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public DateTimeOffset? InvalidatedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
