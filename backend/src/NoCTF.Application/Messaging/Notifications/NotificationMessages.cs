using NoCTF.Domain.Notifications;
using NoCTF.Application.Scoring.Leaderboard;

namespace NoCTF.Application.Messaging;

public sealed record CreateNotification(
    Guid CompetitionId,
    Guid? TeamId,
    Guid? UserId,
    NotificationKind Kind,
    string Payload);

public sealed record DeliverNotification(Guid NotificationId);

public sealed record BloodAwarded(
    Guid CompetitionId,
    Guid CompetitionChallengeId,
    string ChallengeTitle,
    LeaderboardBloodRank BloodRank,
    Guid TeamId,
    string TeamName,
    DateTimeOffset OccurredAt);

public sealed record ChallengePublished(
    Guid CompetitionId,
    Guid CompetitionChallengeId,
    string ChallengeTitle,
    string Direction,
    DateTimeOffset PublishedAt);

public sealed record PublishHintNotification(
    Guid CompetitionId,
    Guid CompetitionChallengeId,
    Guid HintId,
    string ChallengeTitle,
    long Cost,
    DateTimeOffset PublishedAt);

public sealed record TeamBanned(
    Guid CompetitionId,
    Guid TeamId,
    string TeamName,
    DateTimeOffset BannedAt,
    TeamBanAnnouncementKind? AnnouncementKind = null);

public enum TeamBanAnnouncementKind : short
{
    RuleViolation,
    ConfirmedCheating
}

public sealed record TeamBanCorrected(
    Guid CompetitionId,
    Guid TeamId,
    string TeamName,
    Guid BanEventId,
    DateTimeOffset CorrectedAt);

public sealed record TeamBanAppealSubmitted(
    Guid CompetitionId,
    Guid AppealEventId,
    Guid TeamId,
    string TeamName,
    DateTimeOffset SubmittedAt);

public sealed record DeliverCompetitionQuestionNotification(
    Guid CompetitionId,
    Guid ThreadRootId,
    Guid? EntryId,
    Guid[] RecipientUserIds,
    NotificationKind Kind,
    CompetitionQuestionNotificationEvent Event,
    string Title,
    DateTimeOffset OccurredAt);

public enum CompetitionQuestionNotificationEvent : short
{
    Opened,
    HandlerReplied,
    AskerFollowedUp,
    StatusChanged
}
