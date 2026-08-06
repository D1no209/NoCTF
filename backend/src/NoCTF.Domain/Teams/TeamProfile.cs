using System.ComponentModel.DataAnnotations;

namespace NoCTF.Domain.Teams;

/// <summary>Stable team identity and current membership reused across competitions.</summary>
public sealed class TeamProfile
{
    public Guid Id { get; set; }
    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;
    [MaxLength(128)]
    public string NormalizedName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public Guid CaptainId { get; set; }
    public Guid[] MemberIds { get; set; } = [];
    [StringLength(32, MinimumLength = 32)]
    public string InvitationToken { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}
