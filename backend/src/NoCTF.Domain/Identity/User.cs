using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NoCTF.Domain.Identity;

/// <summary>Represents a platform identity.</summary>
public sealed class User : NoCTF.Domain.Shared.IConcurrencyTracked
{
    public Guid Id { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    public string UserName { get; set; } = string.Empty;
    public string NormalizedUserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public UserKind Kind { get; set; }
    public UserRole Role { get; set; }
    public UserAccountStatus AccountStatus { get; set; }
    public int TokenVersion { get; set; }
    public ExternalIdentity? ExternalIdentity { get; set; }
    [NotMapped]
    public Guid? ExternalIdentityProviderId
    {
        get => ExternalIdentity?.ProviderId;
        set
        {
            if (value is null) ExternalIdentity = null;
            else EnsureExternalIdentity().ProviderId = value.Value;
        }
    }
    [NotMapped]
    public SsoProtocol? ExternalIdentityProtocol
    {
        get => ExternalIdentity?.Protocol;
        set { if (value is not null) EnsureExternalIdentity().Protocol = value.Value; }
    }
    [NotMapped, System.Text.Json.Serialization.JsonIgnore]
    public string? ExternalIdentityNamespace
    {
        get => ExternalIdentity?.IdentityNamespace;
        set { if (value is not null) EnsureExternalIdentity().IdentityNamespace = value; }
    }
    [NotMapped, System.Text.Json.Serialization.JsonIgnore]
    public string? ExternalIdentitySubject
    {
        get => ExternalIdentity?.Subject;
        set { if (value is not null) EnsureExternalIdentity().Subject = value; }
    }
    [NotMapped]
    public DateTimeOffset? ExternalIdentityBoundAt
    {
        get => ExternalIdentity?.BoundAt;
        set { if (value is not null) EnsureExternalIdentity().BoundAt = value.Value; }
    }
    [MaxLength(500)]
    public string? Description { get; set; }
    [MaxLength(100), System.Text.Json.Serialization.JsonIgnore]
    public string? SchoolFullName { get; set; }
    [MaxLength(64), System.Text.Json.Serialization.JsonIgnore]
    public string? SchoolStudentNumber { get; set; }
    public Guid? AvatarFileId { get; set; }
    public NoCTF.Domain.Storage.StoredFile? AvatarFile { get; set; }
    public Guid? ProfileCoverFileId { get; set; }
    public NoCTF.Domain.Storage.StoredFile? ProfileCoverFile { get; set; }
    public Guid? WallpaperFileId { get; set; }
    public NoCTF.Domain.Storage.StoredFile? WallpaperFile { get; set; }
    public bool WallpaperEnabled { get; set; }
    public DateTimeOffset? EmailVerifiedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    private ExternalIdentity EnsureExternalIdentity() =>
        ExternalIdentity ??= new ExternalIdentity { UserId = Id };
}

public sealed class ExternalIdentity
{
    public Guid UserId { get; set; }
    public Guid ProviderId { get; set; }
    public SsoProtocol Protocol { get; set; }
    [MaxLength(512)]
    public string IdentityNamespace { get; set; } = string.Empty;
    [MaxLength(255)]
    public string Subject { get; set; } = string.Empty;
    [MaxLength(255)]
    public string NormalizedSubject { get; set; } = string.Empty;
    public DateTimeOffset BoundAt { get; set; }
}
