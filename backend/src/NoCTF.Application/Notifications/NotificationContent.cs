using System.Text.Json.Serialization;
using NoCTF.Application.Messaging;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Challenges.Questions;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Runtime;

namespace NoCTF.Application.Notifications;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(MessageNotificationContent), "message")]
[JsonDerivedType(typeof(CompetitionAnnouncementNotificationContent), "competition-announcement")]
[JsonDerivedType(typeof(QuestionOpenedNotificationContent), "question-opened")]
[JsonDerivedType(typeof(QuestionStatusChangedNotificationContent), "question-status-changed")]
[JsonDerivedType(typeof(CompetitionLifecycleChangedNotificationContent), "competition-lifecycle-changed")]
[JsonDerivedType(typeof(TeamRegistrationChangedNotificationContent), "team-registration-changed")]
[JsonDerivedType(typeof(GameplayFactAdjudicatedNotificationContent), "gameplay-fact-adjudicated")]
[JsonDerivedType(typeof(RuntimeStateChangedNotificationContent), "runtime-state-changed")]
[JsonDerivedType(typeof(StartGateFailedNotificationContent), "start-gate-failed")]
[JsonDerivedType(typeof(ManagementFailureNotificationContent), "management-failure")]
[JsonDerivedType(typeof(BloodAwardedNotificationContent), "blood-awarded")]
[JsonDerivedType(typeof(ChallengePublishedNotificationContent), "challenge-published")]
[JsonDerivedType(typeof(HintPublishedNotificationContent), "hint-published")]
[JsonDerivedType(typeof(TeamBannedNotificationContent), "team-banned")]
[JsonDerivedType(typeof(CheatIncidentDetectedNotificationContent), "cheat-incident-detected")]
[JsonDerivedType(typeof(TeamBanCorrectedNotificationContent), "team-ban-corrected")]
[JsonDerivedType(typeof(TeamBanAppealSubmittedNotificationContent), "team-ban-appeal-submitted")]
[JsonDerivedType(typeof(PlatformAuditExportedNotificationContent), "platform-audit-exported")]
[JsonDerivedType(typeof(UserAccountLifecycleChangedNotificationContent), "user-account-lifecycle-changed")]
[JsonDerivedType(typeof(CompetitionForceDeletedNotificationContent), "competition-force-deleted")]
public abstract record NotificationContent;

public sealed record MessageNotificationContent(
    string? Body,
    CompetitionQuestionStatus? From,
    CompetitionQuestionStatus? To,
    CompetitionQuestionParticipantRole? ActorRole) : NotificationContent;

public sealed record CompetitionAnnouncementNotificationContent(
    string? Title,
    string? Body,
    Guid? TeamId,
    string? TeamName,
    TeamBanAnnouncementKind? AnnouncementKind) : NotificationContent;

public sealed record QuestionOpenedNotificationContent(
    CompetitionQuestionSubject? Subject,
    string? Title,
    string? Body,
    Guid? TeamId,
    Guid? CompetitionChallengeId,
    Guid? GameplayFactId,
    CompetitionQuestionStatus? Status,
    CompetitionQuestionParticipantRole? ActorRole) : NotificationContent;

public sealed record QuestionStatusChangedNotificationContent(
    CompetitionQuestionStatus? From,
    CompetitionQuestionStatus? To,
    CompetitionQuestionParticipantRole? ActorRole) : NotificationContent;

public sealed record CompetitionLifecycleChangedNotificationContent(
    int? State,
    string? Reason) : NotificationContent;

public sealed record TeamRegistrationChangedNotificationContent(
    Guid? TeamId,
    int? State,
    string? Reason) : NotificationContent;

public sealed record GameplayFactAdjudicatedNotificationContent(
    Guid? GameplayFactId,
    GameplayFactState? State,
    GameplayFactResult? Result,
    GameplayFactFailureCode? FailureCode) : NotificationContent;

public sealed record RuntimeStateChangedNotificationContent(
    string? Code,
    Guid? CompetitionChallengeId,
    Guid? RuntimeInstanceId,
    Guid? ChallengeFlagId,
    RuntimeState? State) : NotificationContent;

public sealed record StartGateFailedNotificationContent(string? Code, string? Reason)
    : NotificationContent;

public sealed record ManagementFailureNotificationContent(
    string? Code,
    Guid? CompetitionChallengeId,
    Guid? RuntimeInstanceId,
    Guid? GameplayFactId) : NotificationContent;

public sealed record BloodAwardedNotificationContent(
    Guid? CompetitionId,
    Guid? CompetitionChallengeId,
    string? ChallengeTitle,
    LeaderboardBloodRank? BloodRank,
    Guid? TeamId,
    string? TeamName,
    DateTimeOffset? OccurredAt) : NotificationContent;

public sealed record ChallengePublishedNotificationContent(
    Guid? CompetitionId,
    Guid? CompetitionChallengeId,
    string? ChallengeTitle,
    string? Direction,
    DateTimeOffset? PublishedAt) : NotificationContent;

public sealed record HintPublishedNotificationContent(
    Guid? CompetitionId,
    Guid? CompetitionChallengeId,
    Guid? HintId,
    string? ChallengeTitle,
    long? Cost,
    DateTimeOffset? PublishedAt) : NotificationContent;

public sealed record TeamBannedNotificationContent(
    Guid? CompetitionId,
    Guid? TeamId,
    string? TeamName,
    DateTimeOffset? BannedAt) : NotificationContent;

public sealed record CheatIncidentDetectedNotificationContent(
    Guid? CompetitionId,
    Guid? GameplayFactId,
    Guid? SourceTeamId,
    Guid? OwnerTeamId,
    Guid? ActorUserId,
    Guid? CompetitionChallengeId,
    DateTimeOffset? DetectedAt) : NotificationContent;

public sealed record TeamBanCorrectedNotificationContent(
    Guid? CompetitionId,
    Guid? TeamId,
    string? TeamName,
    DateTimeOffset? CorrectedAt) : NotificationContent;

public sealed record TeamBanAppealSubmittedNotificationContent(
    Guid? CompetitionId,
    Guid? AppealEventId,
    Guid? TeamId,
    string? TeamName,
    DateTimeOffset? SubmittedAt) : NotificationContent;

public sealed record PlatformAuditExportedNotificationContent(
    int? AuditKind,
    Guid? CompetitionId,
    Guid? ActorId,
    DateTimeOffset? From,
    DateTimeOffset? To,
    long? RecordCount) : NotificationContent;

public sealed record UserAccountLifecycleChangedNotificationContent(
    Guid? TargetUserId,
    string? TargetUserName,
    UserAccountLifecycleAction? Action,
    string? Reason,
    bool? Automatic) : NotificationContent;

public sealed record CompetitionForceDeletedReference(short Kind, int Count);

public sealed record CompetitionForceDeletedNotificationContent(
    Guid? CompetitionId,
    string? CompetitionTitle,
    string? Reason,
    IReadOnlyList<CompetitionForceDeletedReference> DeletedReferences) : NotificationContent;
