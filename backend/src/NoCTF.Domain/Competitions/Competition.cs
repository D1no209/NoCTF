using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Shared;

namespace NoCTF.Domain.Competitions;

/// <summary>Represents the common fields of a competition.</summary>
[PersistentHierarchy]
[GeneratePersistentLeaves(typeof(GameMode), "Competition")]
public abstract class Competition : IConcurrencyTracked
{
    protected Competition(GameMode mode) => Mode = mode;
    public Guid Id { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    [MaxLength(160)]
    public string Title { get; set; } = string.Empty;
    [MaxLength(160)]
    public string NormalizedTitle { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? PosterFileId { get; set; }
    public NoCTF.Domain.Storage.StoredFile? PosterFile { get; set; }
    public Guid OwnerId { get; set; }
    public List<CompetitionCollaborator> Collaborators { get; set; } = [];
    [NotMapped]
    public Guid[] ManagerIds
    {
        get => Collaborators.Where(item => item.Role == NoCTF.Domain.Competitions.CompetitionCollaboratorRole.Manager)
            .Select(item => item.UserId).ToArray();
        set => ReplaceCollaborators(NoCTF.Domain.Competitions.CompetitionCollaboratorRole.Manager, value);
    }
    [NotMapped]
    public Guid[] JudgeIds
    {
        get => Collaborators.Where(item => item.Role == NoCTF.Domain.Competitions.CompetitionCollaboratorRole.Judge)
            .Select(item => item.UserId).ToArray();
        set => ReplaceCollaborators(NoCTF.Domain.Competitions.CompetitionCollaboratorRole.Judge, value);
    }
    [NotMapped]
    public Guid[] ObserverIds
    {
        get => Collaborators.Where(item => item.Role == NoCTF.Domain.Competitions.CompetitionCollaboratorRole.Observer)
            .Select(item => item.UserId).ToArray();
        set => ReplaceCollaborators(NoCTF.Domain.Competitions.CompetitionCollaboratorRole.Observer, value);
    }
    public CompetitionAccessMode AccessMode { get; set; }
    public GameMode Mode { get; private set; }
    public CompetitionModeConfiguration? ModeConfiguration { get; set; }
    public List<CompetitionWebhookTarget> WebhookTargets { get; set; } = [];
    [NotMapped]
    public CompetitionWebhookConfiguration WebhookConfiguration
    {
        get => new() { Targets = WebhookTargets };
        set => WebhookTargets = value?.Targets ?? [];
    }
    public bool TracksEnabled { get; set; }
    public List<CompetitionTrackDefinition> Tracks { get; set; } = [];
    public DateTimeOffset? FrozenStartAt { get; set; }
    public DateTimeOffset? HiddenStartAt { get; set; }
    [MaxLength(32)]
    public byte[] FlagDerivationSecret { get; set; } = [];
    public DateTimeOffset StartAt { get; set; }
    public DateTimeOffset EndAt { get; set; }
    public CompetitionStatus Status { get; set; }
    public bool TeamRegistrationAutoApprove { get; set; } = true;
    public bool AllowTeamRegistrationWhileRunning { get; set; }
    public bool PracticeModeEnabled { get; set; }
    public bool WriteUpSubmissionRequired { get; set; }
    public int WriteUpSubmissionDeadlineHours { get; set; }
    public int MaxTeamMembers { get; set; } = 5;
    public int MaxConcurrentRuntimeInstancesPerTeam { get; set; }
    public RuntimeAccessMode RuntimeAccessMode { get; set; }
    public bool TrafficCaptureEnabled { get; set; }
    public long? TrafficCaptureLimitBytes { get; set; }
    public int MaxActiveQuestionsPerTeam { get; set; } = 5;
    public int MaxParticipantMessagesBeforeHandlerReply { get; set; } = 3;
    public bool AllowChallengeOwnersToHandleQuestions { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    private void ReplaceCollaborators(CompetitionCollaboratorRole role, IEnumerable<Guid>? userIds)
    {
        var desired = (userIds ?? []).ToHashSet();
        Collaborators.RemoveAll(item => item.Role == role && !desired.Contains(item.UserId));
        var existing = Collaborators.Where(item => item.Role == role)
            .Select(item => item.UserId)
            .ToHashSet();
        Collaborators.AddRange(desired.Where(userId => !existing.Contains(userId))
            .Select(userId => new CompetitionCollaborator
            {
                CompetitionId = Id,
                UserId = userId,
                Role = role
            }));
    }
}

public enum CompetitionCollaboratorRole : short
{
    Manager,
    Judge,
    Observer
}

public sealed class CompetitionCollaborator
{
    public Guid CompetitionId { get; set; }
    public Guid UserId { get; set; }
    public CompetitionCollaboratorRole Role { get; set; }
}
