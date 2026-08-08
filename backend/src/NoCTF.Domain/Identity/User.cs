using System.ComponentModel.DataAnnotations;

namespace NoCTF.Domain.Identity;

/// <summary>Represents a platform identity.</summary>
public sealed class User
{
    public Guid Id { get; set; }
    [ConcurrencyCheck]
    public long ConcurrencyVersion { get; set; }
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
    public Guid? AvatarFileId { get; set; }
    public NoCTF.Domain.Storage.StoredFile? AvatarFile { get; set; }
    public bool IsEmailPublic { get; set; }
    public DateTimeOffset? EmailVerifiedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
