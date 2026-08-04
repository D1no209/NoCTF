using System.ComponentModel.DataAnnotations;

namespace NoCTF.Domain.Identity;

/// <summary>Represents a platform identity.</summary>
public sealed class User
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string NormalizedUserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public UserKind Kind { get; set; }
    public UserRole Role { get; set; }
    public UserAccountStatus AccountStatus { get; set; }
    public int TokenVersion { get; set; }
    [MaxLength(500)]
    public string? Description { get; set; }
    [MaxLength(1024)]
    public string? AvatarObjectKey { get; set; }
    public DateTimeOffset? EmailVerifiedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
