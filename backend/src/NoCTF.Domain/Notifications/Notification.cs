namespace NoCTF.Domain.Notifications;

using NoCTF.Domain.Challenges.Questions;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Shared;

/// <summary>Represents a durable user notification.</summary>
[PersistentHierarchy]
[GeneratePersistentLeaves(typeof(NotificationKind), "Notification")]
public abstract class Notification
{
    protected Notification(NotificationKind kind) => Kind = kind;

    public static readonly Guid PlatformAdministratorsTargetId =
        Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");

    public Guid Id { get; set; }
    public NotificationSourceType SourceType { get; set; }
    public Guid? SourceId { get; set; }
    public NotificationTargetType TargetType { get; set; }
    public Guid TargetId { get; set; }
    public NotificationKind Kind { get; private set; }
    public string? SourceEventKey { get; set; }
    public string? Subject { get; set; }
    public string? Title { get; set; }
    public string? Body { get; set; }
    public string? Code { get; set; }
    public string? Reason { get; set; }
    public string? Direction { get; set; }
    public string? IpAddress { get; set; }
    public string? ChallengeTitle { get; set; }
    public string? TeamName { get; set; }
    public string? UserName { get; set; }
    public string? ProviderName { get; set; }
    public Guid? CompetitionId { get; set; }
    public Guid? CompetitionChallengeId { get; set; }
    public Guid? TeamId { get; set; }
    public Guid? UserId { get; set; }
    public Guid? ActorUserId { get; set; }
    public Guid? GameplayFactId { get; set; }
    public Guid? RuntimeInstanceId { get; set; }
    public Guid? ChallengeFlagId { get; set; }
    public Guid? HintId { get; set; }
    public Guid? AppealEventId { get; set; }
    public Guid? EntryId { get; set; }
    public Guid? SourceTeamId { get; set; }
    public Guid? OwnerTeamId { get; set; }
    public Guid? SsoProviderId { get; set; }
    public Guid? JwtId { get; set; }
    public long? Value { get; set; }
    public int? Count { get; set; }
    public int? ActionValue { get; set; }
    public int? StateValue { get; set; }
    public int? PreviousStateValue { get; set; }
    public bool? Automatic { get; set; }
    public DateTimeOffset? PayloadOccurredAt { get; set; }
    public DateTimeOffset? PayloadExpiresAt { get; set; }
    public DateTimeOffset? RangeFrom { get; set; }
    public DateTimeOffset? RangeTo { get; set; }
    public CompetitionQuestionSubject? QuestionSubject { get; set; }
    public CompetitionQuestionStatus? QuestionStatus { get; set; }
    public CompetitionQuestionStatus? PreviousQuestionStatus { get; set; }
    public CompetitionQuestionParticipantRole? QuestionActorRole { get; set; }
    public UserAccountLifecycleAction? UserLifecycleAction { get; set; }
    public SsoProtocol? SsoProtocol { get; set; }
    public RuntimeState? RuntimeState { get; set; }
    public GameplayFactState? GameplayFactState { get; set; }
    public GameplayFactResult? GameplayFactResult { get; set; }
    public GameplayFactFailureCode? GameplayFactFailureCode { get; set; }
    public List<NotificationReferenceCount> ReferenceCounts { get; set; } = [];
    public DateTimeOffset SentAt { get; set; }
    public EntityReferenceKind? RelatedType { get; set; }
    public Guid? RelatedId { get; set; }
    public Guid? ThreadRootId { get; set; }
    public Guid? ReplyToId { get; set; }
}

public enum NotificationSourceType : short
{
    System,
    User,
    Competition,
    Team,
    Platform
}

public enum NotificationTargetType : short
{
    User,
    CompetitionCollaborators,
    CompetitionParticipants,
    TeamMembers,
    PlatformAdministrators
}

public enum NotificationKind : short
{
    Message,
    CompetitionAnnouncement,
    QuestionOpened,
    QuestionStatusChanged,
    CompetitionLifecycleChanged,
    TeamRegistrationChanged,
    GameplayFactAdjudicated,
    RuntimeStateChanged,
    StartGateFailed,
    ManagementFailure,
    BloodAwarded,
    ChallengePublished,
    HintPublished,
    TeamBanned,
    CheatIncidentDetected,
    TeamBanCorrected,
    TeamBanAppealSubmitted,
    PlatformAuditExported,
    UserAccountLifecycleChanged,
    CompetitionForceDeleted,
    AuthenticationSecurityActivity,
    PlatformUserAccessTokenIssued,
    PlatformUserAccessTokenRevoked,
    PlatformUserTokensInvalidated,
    SsoProviderConfigurationChanged,
    SsoExternalIdentityBindingChanged
}

public sealed class NotificationReferenceCount
{
    public Guid NotificationId { get; set; }
    public short ReferenceKind { get; set; }
    public int Count { get; set; }
}
