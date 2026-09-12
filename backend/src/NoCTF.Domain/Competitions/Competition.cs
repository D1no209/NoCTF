using System.ComponentModel.DataAnnotations;

namespace NoCTF.Domain.Competitions;

/// <summary>Represents the common fields of a competition.</summary>
public sealed class Competition
{
    public Guid Id { get; set; }
    [MaxLength(160)]
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? PosterFileId { get; set; }
    public NoCTF.Domain.Storage.StoredFile? PosterFile { get; set; }
    public Guid OwnerId { get; set; }
    public Guid[] ManagerIds { get; set; } = [];
    public Guid[] JudgeIds { get; set; } = [];
    public Guid[] ObserverIds { get; set; } = [];
    public CompetitionAccessMode AccessMode { get; set; }
    public GameMode Mode { get; set; }
    public string ConfigurationJson { get; set; } = string.Empty;
    public bool TracksEnabled { get; set; }
    public string? TrackConfigurationJson { get; set; }
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
    public int MaxActiveQuestionsPerTeam { get; set; } = 5;
    public int MaxParticipantMessagesBeforeHandlerReply { get; set; } = 3;
    public bool AllowChallengeOwnersToHandleQuestions { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}
