using NoCTF.Bot.NoCtf;

namespace NoCTF.Bot.Persistence;

public sealed record GroupSubscription(
    long GroupId,
    Guid CompetitionId,
    bool BroadcastEnabled,
    bool ScoreboardEnabled,
    bool BloodEnabled,
    string? SuspendedReason,
    DateTimeOffset UpdatedAt);

public sealed record CompetitionSnapshot(
    Guid CompetitionId,
    CompetitionStatus Status,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    string ContentHash,
    DateTimeOffset UpdatedAt);

public sealed record ChallengeSnapshot(
    Guid CompetitionId,
    Guid ChallengeId,
    string Title,
    bool Published,
    string ContentHash);

public sealed record TeamSnapshot(
    Guid CompetitionId,
    Guid TeamId,
    string TeamName,
    int? Rank,
    long Score,
    string AchievementHash,
    string AchievementsJson);

public enum OutboundMessageState
{
    Pending,
    Sending,
    Completed,
    DeadLetter
}

public sealed record OutboundMessage(
    Guid Id,
    string DedupeKey,
    long GroupId,
    string Payload,
    OutboundMessageState State,
    int AttemptCount,
    DateTimeOffset NextAttemptAt);
