using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NoCTF.Domain.Teams;

/// <summary>Represents a competition-scoped team.</summary>
public sealed class Team : NoCTF.Domain.Shared.IConcurrencyTracked
{
    public Guid Id { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    public Guid CompetitionId { get; set; }
    [MaxLength(64)]
    public string TrackKey { get; set; } = NoCTF.Domain.Competitions.CompetitionTrackConfiguration.DefaultTrackKey;
    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;
    [MaxLength(128)]
    public string NormalizedName { get; set; } = string.Empty;
    public Guid? AvatarFileId { get; set; }
    public NoCTF.Domain.Storage.StoredFile? AvatarFile { get; set; }
    public Guid? WriteUpFileId { get; set; }
    public NoCTF.Domain.Storage.StoredFile? WriteUpFile { get; set; }
    public Guid? WriteUpSubmittedByUserId { get; set; }
    public DateTimeOffset? WriteUpSubmittedAt { get; set; }
    public Guid CaptainId { get; set; }
    public TeamCaptain? CaptainMembership { get; set; }
    public List<TeamMember> Members { get; set; } = [];
    [NotMapped]
    public Guid[] MemberIds
    {
        get => Members.Select(member => member.UserId).ToArray();
        set
        {
            var desired = (value ?? []).ToHashSet();
            Members.RemoveAll(member => !desired.Contains(member.UserId));
            var existing = Members.Select(member => member.UserId).ToHashSet();
            Members.AddRange(desired.Where(userId => !existing.Contains(userId))
                .Select(userId => new TeamMember { TeamId = Id, UserId = userId }));
        }
    }
    [StringLength(32, MinimumLength = 32)]
    public string InvitationToken { get; set; } = string.Empty;
    public bool IsLocked { get; set; }
    public TeamRegistrationStatus RegistrationStatus { get; set; }
    public DateTimeOffset RegisteredAt { get; set; }
    public bool IsBanned { get; set; }
    public DateTimeOffset? BannedAt { get; set; }
    public Guid? BannedById { get; set; }
    public string? BanReason { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

public sealed class TeamMember
{
    public Guid TeamId { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid UserId { get; set; }
    public ActiveTeamMembership? ActiveMembership { get; set; }
}

/// <summary>Reserves a user's current team in one competition while retaining archived membership.</summary>
public sealed class ActiveTeamMembership
{
    public Guid CompetitionId { get; set; }
    public Guid UserId { get; set; }
    public Guid TeamId { get; set; }
}

public sealed class TeamCaptain
{
    public Guid TeamId { get; set; }
    public Guid UserId { get; set; }
}
