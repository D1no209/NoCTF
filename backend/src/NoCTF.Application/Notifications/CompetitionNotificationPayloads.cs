using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.Messaging;

namespace NoCTF.Application.Notifications;

public sealed record BloodAwardedPayload(
    Guid CompetitionId,
    Guid CompetitionChallengeId,
    string ChallengeTitle,
    LeaderboardBloodRank BloodRank,
    Guid TeamId,
    string TeamName,
    DateTimeOffset OccurredAt);

public sealed record ChallengePublishedPayload(
    Guid CompetitionId,
    Guid CompetitionChallengeId,
    string ChallengeTitle,
    string Direction,
    DateTimeOffset PublishedAt);

public sealed record HintPublishedPayload(
    Guid CompetitionId,
    Guid CompetitionChallengeId,
    Guid HintId,
    string ChallengeTitle,
    long Cost,
    DateTimeOffset PublishedAt);

public sealed record TeamBannedPayload(
    Guid CompetitionId,
    Guid TeamId,
    string TeamName,
    DateTimeOffset BannedAt);

public sealed record CompetitionQuestionActivityPayload(
    Guid CompetitionId,
    Guid QuestionId,
    Guid? EntryId,
    CompetitionQuestionNotificationEvent Event,
    string Title,
    DateTimeOffset OccurredAt);
