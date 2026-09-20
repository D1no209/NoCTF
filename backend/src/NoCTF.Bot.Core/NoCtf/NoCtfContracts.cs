using System.Text.Json.Serialization;

namespace NoCTF.Bot.NoCtf;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CompetitionStatus
{
    Draft,
    Visible,
    Published,
    Running,
    Paused,
    Finished
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CompetitionAccessMode { Public, StaffOnly }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LeaderboardVisibility { Normal, Frozen, Blackout }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LeaderboardDataScope { Live, Frozen, Hidden }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ScoreboardRankingState { Eligible, Banned, Disqualified }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ScoreboardEntryKind
{
    Solve,
    Attack,
    Defense,
    Availability,
    Control,
    Penalty,
    BloodAward,
    Hint,
    ManualAdjustment
}

public sealed record CurrentUser(
    Guid UserId,
    string UserName,
    string Role,
    string Kind);

public sealed record Competition(
    Guid Id,
    string Title,
    string? Description,
    string Mode,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    CompetitionStatus Status,
    LeaderboardVisibility LeaderboardVisibility,
    CompetitionAccessMode AccessMode);

public sealed record ChallengeList(
    IReadOnlyList<Challenge> Items,
    LeaderboardVisibility LeaderboardVisibility,
    LeaderboardDataScope DataScope);

public sealed record Challenge(
    Guid Id,
    string Title,
    string? CustomTitle,
    string? Description,
    string Direction,
    int Order,
    bool IsPublished,
    DateTimeOffset UpdatedAt)
{
    public string DisplayTitle => string.IsNullOrWhiteSpace(CustomTitle) ? Title : CustomTitle;
}

public sealed record ScoreboardSnapshot(
    Guid CompetitionId,
    string Version,
    string SchemaRevision,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<ScoreboardTeam> Teams,
    LeaderboardVisibility Visibility,
    LeaderboardDataScope DataScope,
    DateTimeOffset? DataAsOf);

public sealed record ScoreboardTeam(
    Guid TeamId,
    string TeamName,
    string TrackKey,
    int? Rank,
    ScoreboardRankingState RankingState,
    long TotalScore,
    IReadOnlyList<ScoreboardAchievement>? Achievements);

public sealed record ScoreboardAchievement(
    Guid CompetitionChallengeId,
    ScoreboardEntryKind Kind,
    Guid? UserId,
    string? DisplayName,
    DateTimeOffset OccurredAt);

public sealed record ScoreboardUpdated(
    Guid CompetitionId,
    string Version,
    string SchemaRevision,
    string ChallengeCatalogRevision);

public sealed record CompetitionLifecycleChanged(
    Guid CompetitionId,
    CompetitionStatus From,
    CompetitionStatus To,
    DateTimeOffset OccurredAt);

public sealed record CompetitionEventChanged(
    Guid CompetitionId,
    Guid EventId,
    string Kind,
    string Level,
    DateTimeOffset OccurredAt);

public sealed record CompetitionAnnouncement(
    Guid Id,
    string Title,
    string Body,
    DateTimeOffset PublishedAt);

public sealed record CompetitionAnnouncementList(
    IReadOnlyList<CompetitionAnnouncement> Items,
    string? NextCursor);

public enum NoCtfReadState
{
    Available,
    Processing,
    NotFound,
    Unauthorized,
    Unavailable
}

public sealed record NoCtfReadResult<T>(
    NoCtfReadState State,
    T? Value = default,
    TimeSpan? RetryAfter = null)
{
    public static NoCtfReadResult<T> Available(T value) =>
        new(NoCtfReadState.Available, value);
}
