using NoCTF.Domain.Notifications;
using NoCTF.Application.Scoring.Leaderboard;

namespace NoCTF.Application.Messaging;

public sealed record CreateNotification(
    Guid CompetitionId,
    Guid? TeamId,
    Guid? UserId,
    NotificationKind Kind,
    string Payload,
    long ProcessingVersion);

public sealed record DeliverNotification(
    Guid NotificationId,
    long ProcessingVersion);

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
    DateTimeOffset PublishedAt,
    int Revision);

public sealed record PublishHintNotification(
    Guid CompetitionId,
    Guid CompetitionChallengeId,
    Guid HintId,
    string ChallengeTitle,
    long Cost,
    DateTimeOffset PublishedAt,
    int PublicationRevision);

public sealed record TeamBanned(
    Guid CompetitionId,
    Guid TeamId,
    string TeamName,
    DateTimeOffset BannedAt);
