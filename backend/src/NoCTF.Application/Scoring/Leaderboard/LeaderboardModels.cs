using NoCTF.Domain.Competitions;

namespace NoCTF.Application.Scoring.Leaderboard;

public enum LeaderboardBloodRank
{
    First = 1,
    Second = 2,
    Third = 3
}

/// <summary>
/// The sparse value of one team × competition-challenge cell.
/// An absent cell means that the team has no public result for that challenge.
/// </summary>
public sealed record LeaderboardCell(
    Guid CompetitionChallengeId,
    long Score,
    DateTimeOffset? SolvedAt,
    string? SolverName,
    LeaderboardBloodRank? BloodRank)
{
    public long? AttackScore { get; init; }
    public long? DefenseScore { get; init; }
}

/// <summary>Projection-only cell before it is attached to a ranked team row.</summary>
public sealed record LeaderboardCellFact(
    Guid TeamId,
    Guid CompetitionChallengeId,
    long Score,
    DateTimeOffset? SolvedAt,
    string? SolverName)
{
    public long? AttackScore { get; init; }
    public long? DefenseScore { get; init; }
}

public sealed record LeaderboardChallengeInfo(
    Guid CompetitionChallengeId,
    string Title,
    string Direction,
    long? CurrentScore,
    long? CurrentBreakScore,
    long? CurrentFixScore);

public sealed record LeaderboardEntry(
    int Rank,
    Guid TeamId,
    string TeamName,
    long Score,
    int SolveCount,
    DateTimeOffset? LastScoreAt,
    string TrackKey = "default")
{
    public long? AttackScore { get; init; }
    public long? DefenseScore { get; init; }
    public long? PenaltyScore { get; init; }

    /// <summary>Only solved/corrected cells are serialized; the matrix is sparse.</summary>
    public IReadOnlyList<LeaderboardCell> Cells { get; init; } = [];
}

public enum ScoreboardRankingState
{
    Eligible,
    Banned,
    Disqualified
}

public enum ScoreboardRoundState
{
    Pending,
    Running,
    Settling,
    Settled
}

public enum ScoreboardScoreState
{
    Pending,
    Provisional,
    Settled
}

public enum ScoreboardBreakdownKind
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

public enum ScoreboardEntryOutcome
{
    Pending,
    Succeeded,
    Failed,
    Rejected
}

public enum ScoreboardAward
{
    FirstBlood,
    SecondBlood,
    ThirdBlood
}

public enum ScoreboardAdjustmentKind
{
    ManualAdjustment,
    CompetitionPenalty,
    BanRecalculation
}

public sealed record ScoreboardChallengeCatalogItem(
    Guid CompetitionChallengeId,
    string Title,
    string Direction,
    string Category,
    int Order,
    bool IsPublished,
    int Revision);

public sealed record ScoreboardChallengeCatalog(
    Guid CompetitionId,
    long Revision,
    IReadOnlyList<ScoreboardChallengeCatalogItem> Challenges);

public sealed record ScoreboardRound(
    Guid Id,
    int Number,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    DateTimeOffset? SettledAt,
    ScoreboardRoundState State);

public sealed record ScoreboardColumn(
    int Index,
    Guid CompetitionChallengeId,
    Guid? RoundId);

public sealed record ScoreboardSchema(
    Guid CompetitionId,
    GameMode Mode,
    long Revision,
    long ChallengeCatalogRevision,
    IReadOnlyList<ScoreboardRound> Rounds,
    IReadOnlyList<ScoreboardColumn> Columns);

public sealed record ScoreboardActor(
    int Index,
    Guid UserId,
    string DisplayName);

public sealed record ScoreboardBreakdown(
    ScoreboardBreakdownKind Kind,
    int SuccessfulCount,
    int AttemptCount,
    long EarnedPoints,
    long DeductedPoints,
    long NetPoints);

public sealed record ScoreboardSlotEntry(
    Guid Id,
    ScoreboardEntryKind Kind,
    ScoreboardEntryOutcome Outcome,
    int? ActorIndex,
    Guid? TargetTeamId,
    DateTimeOffset OccurredAt,
    DateTimeOffset? SettledAt,
    long? EarnedPoints,
    long? DeductedPoints,
    long? NetPoints,
    ScoreboardAward? Award = null,
    long AwardPoints = 0);

public sealed record ScoreboardAdjustment(
    Guid Id,
    ScoreboardAdjustmentKind Kind,
    DateTimeOffset OccurredAt,
    int? ActorIndex,
    long EarnedPoints,
    long DeductedPoints,
    long NetPoints);

public sealed record ScoreboardSlot(
    int ColumnIndex,
    ScoreboardScoreState ScoreState,
    long? EarnedPoints,
    long? DeductedPoints,
    long? NetPoints,
    int EntryCount,
    IReadOnlyList<ScoreboardBreakdown> Breakdowns,
    IReadOnlyList<ScoreboardSlotEntry> Entries);

public sealed record ScoreboardTeam(
    Guid TeamId,
    string TeamName,
    string TrackKey,
    int? Rank,
    ScoreboardRankingState RankingState,
    long TotalScore,
    int GlobalAdjustmentCount,
    IReadOnlyList<ScoreboardAdjustment> GlobalAdjustments,
    IReadOnlyList<ScoreboardSlot> Slots);

public sealed record ScoreboardTrack(
    string Key,
    string Name,
    bool IsInternal,
    bool VisibleOnLeaderboard);

public sealed record ScoreboardSnapshot(
    Guid CompetitionId,
    long Version,
    long SchemaRevision,
    DateTimeOffset GeneratedAt,
    Guid? CurrentRoundId,
    IReadOnlyList<ScoreboardActor> Actors,
    IReadOnlyList<ScoreboardTeam> Teams)
{
    public IReadOnlyList<ScoreboardTrack> Tracks { get; init; } = [];
    public CompetitionLeaderboardVisibility Visibility { get; init; }
    public LeaderboardDataScope DataScope { get; init; }
    public DateTimeOffset? DataAsOf { get; init; }
}

public sealed record ScoreboardProjection(
    ScoreboardChallengeCatalog ChallengeCatalog,
    ScoreboardSchema Schema,
    ScoreboardSnapshot Snapshot);
