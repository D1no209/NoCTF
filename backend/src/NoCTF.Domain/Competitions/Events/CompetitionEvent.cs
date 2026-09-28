using System.ComponentModel.DataAnnotations;
using NoCTF.Domain.Shared;

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
    GameplayFactReceived,
    GameplayFactAdjudicated,
    ScoringRecorded,
    FirstBloodAwarded,
    SecondBloodAwarded,
    ThirdBloodAwarded,
    RuntimeCreated,
    RuntimeStateChanged,
    RuntimeExtended,
    RuntimeReset,
    RuntimePortAllocated,
    ProtectedGameplayFactValueAccessed,
    CheatIncidentDetected,
    CheatIncidentConfirmed,
    CheatIncidentDismissed,
    CheatIncidentSuperseded,
    CheatIncidentCorrected,
    CompetitionArchiveExported,
    TeamBanAppealSubmitted,
    TeamBanAppealUpheld,
    TeamBanAppealAccepted,
    TeamBanCorrectionPublished,
    RuntimeForceTerminationRequested,
    RuntimeForceTerminationCompleted,
    RuntimeForceTerminationFailed,
    AnnouncementPublished,
    QuestionOpened,
    QuestionReplied,
    QuestionStatusChanged,
    ChallengeDescriptionUpdated,
    TrackConfigurationUpdated,
    TeamTrackChanged,
    AwdpBreakAttempted,
    AwdpFixAttempted,
    AwdpBreakResolved,
    AwdpFixResolved,
    GameplayFactPatchDownloaded,
    TrackRegistrationPolicyUpdated,
    CompetitionAudienceChanged,
    TeamWriteUpSubmitted,
    RuntimeTrafficCaptureStored,
    RuntimeTrafficCaptureDeleted
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

[PersistentHierarchy]
[GeneratePersistentLeaves(typeof(CompetitionEventKind), "Event")]
public abstract class CompetitionEvent
{
    protected CompetitionEvent(CompetitionEventKind kind) => Kind = kind;

    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public CompetitionEventKind Kind { get; private set; }
    public CompetitionEventLevel Level { get; set; }
    public CompetitionEventVisibility Visibility { get; set; }
    public Guid? ActorUserId { get; set; }
    public EntityReferenceKind SubjectType { get; set; }
    public Guid SubjectId { get; set; }
    public EntityReferenceKind? RelatedType { get; set; }
    public Guid? RelatedId { get; set; }
    public Guid? ParentEventId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public Guid? RelatedUserId { get; set; }
    public Guid? TeamId { get; set; }
    public Guid? CompetitionChallengeId { get; set; }
    public Guid? HintId { get; set; }
    public Guid? RuntimeInstanceId { get; set; }
    public Guid? GameplayFactId { get; set; }
    public Guid? QuestionId { get; set; }
    public CompetitionStatus? CompetitionStatus { get; set; }
    public CompetitionStatus? PreviousCompetitionStatus { get; set; }
    public CompetitionLeaderboardVisibility? LeaderboardVisibility { get; set; }
    public CompetitionLeaderboardVisibility? PreviousLeaderboardVisibility { get; set; }
    public CompetitionAccessMode? CompetitionAccessMode { get; set; }
    public CompetitionAccessMode? PreviousCompetitionAccessMode { get; set; }
    public CompetitionAudienceChangeKind? CompetitionAudienceChangeKind { get; set; }
    public NoCTF.Domain.Teams.TeamRegistrationStatus? TeamRegistrationStatus { get; set; }
    public NoCTF.Domain.Gameplay.GameplayFactKind? GameplayFactKind { get; set; }
    public NoCTF.Domain.Gameplay.GameplayFactState? GameplayFactState { get; set; }
    public NoCTF.Domain.Gameplay.GameplayFactResult? GameplayFactResult { get; set; }
    public NoCTF.Domain.Runtime.RuntimeState? RuntimeState { get; set; }
    public NoCTF.Domain.Runtime.RuntimeCleanupResult? RuntimeCleanupResult { get; set; }
    public NoCTF.Domain.Challenges.Questions.CompetitionQuestionStatus? QuestionStatus { get; set; }
    public int? HostPort { get; set; }
    [MaxLength(512)] public string? Reason { get; set; }
    [MaxLength(64)] public string? TrackKey { get; set; }
    [MaxLength(64)] public string? PreviousTrackKey { get; set; }
    public bool Automatic { get; set; }
    public Guid? PatchUploadId { get; set; }
    public NoCTF.Domain.Gameplay.AwdpFixOutcome? AwdpFixOutcome { get; set; }
    public NoCTF.Domain.Gameplay.GameplayFactFailureCode? GameplayFactFailureCode { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public bool? TrackConfigurationEnabled { get; set; }
    [MaxLength(64)] public string? DefaultTrackKey { get; set; }
    public int? ReassignedTeamCount { get; set; }
    public List<CompetitionEventTrackKey> TrackKeys { get; set; } = [];
    public bool? IncludesProtectedFlags { get; set; }
    public DateTimeOffset? FrozenStartAt { get; set; }
    public DateTimeOffset? HiddenStartAt { get; set; }
    public Guid? TrafficSegmentId { get; set; }
    public int? TrafficBindingIndex { get; set; }
    [MaxLength(128)] public string? TrafficConnectionId { get; set; }
    public DateTimeOffset? TrafficStartedAt { get; set; }
    public DateTimeOffset? TrafficEndedAt { get; set; }
    [MaxLength(64)] public string? TrafficClientAddress { get; set; }
    public int? TrafficClientPort { get; set; }
    [MaxLength(64)] public string? TrafficDestinationAddress { get; set; }
    public int? TrafficDestinationPort { get; set; }
    public long? TrafficClientToRuntimeBytes { get; set; }
    public long? TrafficRuntimeToClientBytes { get; set; }
    public long? TrafficCapturedBytes { get; set; }
    public bool? TrafficTruncated { get; set; }
}

public sealed class CompetitionEventTrackKey
{
    public Guid Id { get; set; }
    public int Position { get; set; }
    [MaxLength(64)] public string Value { get; set; } = string.Empty;
}
