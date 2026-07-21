using NoCTF.Domain.Shared;

namespace NoCTF.Domain.Competitions;

/// <summary>Represents the common fields of a competition.</summary>
public sealed class Competition
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid OwnerId { get; set; }
    public GameMode Mode { get; set; }
    public string ConfigurationJson { get; set; } = """{"schemaVersion":1}""";
    public int ConfigurationRevision { get; set; }
    public DateTimeOffset ConfigurationUpdatedAt { get; set; }
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    public CompetitionStatus Status { get; set; }
    public bool TeamRegistrationAutoApprove { get; set; } = true;
    public int MaxTeamMembers { get; set; } = 5;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public SoftDeleteState Deletion { get; set; } = new();
    public List<CompetitionCollaborator> Collaborators { get; set; } = [];
    public List<CompetitionLifecycleAudit> LifecycleAudits { get; set; } = [];
}
