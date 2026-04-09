using NoCTF.PluginBase;

namespace NoCTF.Core;

public class User
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class Team : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public Guid CaptainId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class TeamMember
{
    public Guid Id { get; set; }
    public Guid TeamId { get; set; }
    public Guid UserId { get; set; }
    public TeamMemberRole Role { get; set; }
    public DateTime JoinedAt { get; set; }
}

public class Competition : ITenantEntity
{
    // CompetitionId == Id (tenant is itself)
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public GameModeType GameModeType { get; set; }
    public Guid OwnerId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public CompetitionStatus Status { get; set; }
}

public class CompetitionCollaborator
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid UserId { get; set; }
    public CollaboratorRole Role { get; set; }
    public DateTime AddedAt { get; set; }
}

public class Challenge : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string TypeId { get; set; } = string.Empty;
    public PointsConfig PointsConfig { get; set; } = new();
    public string? AttachmentUrl { get; set; }
    public string? ContainerImage { get; set; }
    public string? FlagSecret { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class Submission : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid TeamId { get; set; }
    public Guid ChallengeId { get; set; }
    public Guid UserId { get; set; }
    public string FlagContent { get; set; } = string.Empty;
    public bool IsCorrect { get; set; }
    public DateTime SubmittedAt { get; set; }
    public string IpAddress { get; set; } = string.Empty;
}

public class ScoreEvent : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid TeamId { get; set; }
    public Guid? ChallengeId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public int PointsDelta { get; set; }
    public string? Reason { get; set; }
    public DateTime Timestamp { get; set; }
}
