namespace NoCTF.Domain.Notifications;

using NoCTF.Domain.Shared;

/// <summary>Represents a durable user notification.</summary>
public sealed class Notification
{
    public static readonly Guid PlatformAdministratorsTargetId =
        Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");

    public Guid Id { get; set; }
    public NotificationSourceType SourceType { get; set; }
    public Guid? SourceId { get; set; }
    public NotificationTargetType TargetType { get; set; }
    public Guid TargetId { get; set; }
    public NotificationKind Kind { get; set; }
    public string ContentJson { get; set; } = "{\"schemaVersion\":1}";
    public DateTimeOffset SentAt { get; set; }
    public EntityReferenceKind? RelatedType { get; set; }
    public Guid? RelatedId { get; set; }
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
    DataExportReady,
    DataExportFailed,
    UserAccountLifecycleChanged,
    CompetitionForceDeleted
}
