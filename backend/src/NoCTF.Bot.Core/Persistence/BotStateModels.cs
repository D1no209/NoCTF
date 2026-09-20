using NoCTF.Bot.NoCtf;

namespace NoCTF.Bot.Persistence;

public sealed record GroupSubscription(
    string ProviderId,
    string GroupId,
    Guid CompetitionId,
    bool BroadcastEnabled,
    bool ScoreboardEnabled,
    bool BloodEnabled,
    string? SuspendedReason,
    DateTimeOffset UpdatedAt);

public sealed record GroupAccess(
    string ProviderId,
    string GroupId,
    DateTimeOffset? MasterAuthorizedAt,
    bool Enabled,
    DateTimeOffset UpdatedAt);

public sealed record AnnouncementCheckpoint(
    string ProviderId,
    string GroupId,
    Guid CompetitionId,
    DateTimeOffset PublishedAt,
    Guid AnnouncementId);

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
    string ProviderId,
    string GroupId,
    string Payload,
    OutboundMessageState State,
    int AttemptCount,
    DateTimeOffset NextAttemptAt);
