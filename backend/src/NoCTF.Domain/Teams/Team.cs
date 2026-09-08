using System.ComponentModel.DataAnnotations;

namespace NoCTF.Domain.Teams;

/// <summary>Represents a competition-scoped team.</summary>
public sealed class Team
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    [MaxLength(64)]
    public string TrackKey { get; set; } = NoCTF.Domain.Competitions.CompetitionTrackConfiguration.DefaultTrackKey;
    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;
    public Guid? AvatarFileId { get; set; }
    public NoCTF.Domain.Storage.StoredFile? AvatarFile { get; set; }
    public Guid CaptainId { get; set; }
    public Guid[] MemberIds { get; set; } = [];
    [StringLength(32, MinimumLength = 32)]
    public string InvitationToken { get; set; } = string.Empty;
    public bool IsLocked { get; set; }
    public TeamRegistrationStatus RegistrationStatus { get; set; }
    /// <summary>Created for post-competition practice; never part of official scoring or rankings.</summary>
    public bool IsPracticeTeam { get; set; }
    public DateTimeOffset RegisteredAt { get; set; }
    public bool IsBanned { get; set; }
    public DateTimeOffset? BannedAt { get; set; }
    public Guid? BannedById { get; set; }
    public string? BanReason { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}
