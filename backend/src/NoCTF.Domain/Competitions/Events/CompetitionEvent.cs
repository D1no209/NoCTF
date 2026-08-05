using System.ComponentModel.DataAnnotations;
using NoCTF.Domain.Challenges.Questions;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Submissions;
using NoCTF.Domain.Teams;

namespace NoCTF.Domain.Competitions.Events;

public enum CompetitionEventKind : short
{
    CompetitionCreated,
    CompetitionUpdated,
    CompetitionDeleted,
    CompetitionLifecycleChanged,
    LeaderboardVisibilityChanged,
    ChallengeCreated,
    ChallengeUpdated,
    ChallengePublished,
    ChallengeUnpublished,
    ChallengeDeleted,
    HintPublished,
    HintUnlocked,
    TeamRegistered,
    TeamRegistrationChanged,
    TeamUpdated,
    TeamDeleted,
    TeamMemberJoined,
    TeamMemberRemoved,
    TeamCaptainTransferred,
    TeamBanned,
    TeamUnbanned,
    SubmissionReceived,
    SubmissionEvaluated,
    ScoringRecorded,
    FirstBloodAwarded,
    SecondBloodAwarded,
    ThirdBloodAwarded,
    RuntimeCreated,
    RuntimeStateChanged,
    RuntimeExtended,
    RuntimeReset,
    RuntimePortAllocated,
    AnnouncementPublished,
    QuestionOpened,
    QuestionReplied,
    QuestionStatusChanged,
    QuestionPublished,
    ProtectedSubmissionFlagAccessed,
    CheatIncidentDetected,
    CheatIncidentConfirmed,
    CheatIncidentDismissed,
    CheatIncidentSuperseded,
    CheatIncidentCorrected
}

public enum CompetitionEventLevel : short
{
    Information,
    Warning,
    Error
}

public enum CompetitionEventVisibility : short
{
    Public,
    Team,
    Staff
}

public sealed class CompetitionEvent
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public CompetitionEventKind Kind { get; set; }
    public CompetitionEventLevel Level { get; set; }
    public CompetitionEventVisibility Visibility { get; set; }
    public Guid? ActorUserId { get; set; }
    public Guid? RelatedUserId { get; set; }
    public Guid? TeamId { get; set; }
    public Guid? CompetitionChallengeId { get; set; }
    public Guid? HintId { get; set; }
    public Guid? RuntimeInstanceId { get; set; }
    public Guid? SubmissionId { get; set; }
    public Guid? ScoringEventId { get; set; }
    public Guid? QuestionId { get; set; }
    public CompetitionStatus? CompetitionStatus { get; set; }
    public CompetitionLeaderboardVisibility? LeaderboardVisibility { get; set; }
    public TeamRegistrationStatus? TeamRegistrationStatus { get; set; }
    public SubmissionKind? SubmissionKind { get; set; }
    public SubmissionEvaluationState? SubmissionState { get; set; }
    public ScoringEventKind? ScoringEventKind { get; set; }
    public ScoringResult? ScoringResult { get; set; }
    public RuntimeState? RuntimeState { get; set; }
    public CompetitionQuestionStatus? QuestionStatus { get; set; }
    public int? RuntimeGeneration { get; set; }
    public int? HostPort { get; set; }
    [MaxLength(512)]
    public string? Reason { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
