using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;

namespace NoCTF.Application.Scoring.Leaderboard;

public static class ScoreboardRoundWindow
{
    public const int DefaultSize = 50;
}

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
    bool IsPublished);

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
    IReadOnlyList<ScoreboardColumn> Columns)
{
    public int? RoundWindowStart { get; init; }
    public int? RoundWindowEnd { get; init; }
    public int? LatestRound { get; init; }
}

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

public enum ScoreboardOperationState
{
    None,
    Failed,
    Succeeded
}

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

public sealed record ScoreboardEntryAllocation(
    Guid TeamId,
    int ColumnIndex,
    ScoreboardSlotEntry Entry)
{
    /// <summary>
    /// Identifies the bounded PostgreSQL fact group represented by this allocation.
    /// A null source denotes a synthetic settlement/system entry.
    /// </summary>
    public ScoreboardEntrySource? Source { get; init; }
}

public sealed record ScoreboardEntrySource(
    GameplayFactKind Kind,
    GameplayFactState State,
    GameplayFactResult? Result,
    GameplayFactFailureCode? FailureCode,
    GameplayFactReferenceKind? ReferenceKind,
    Guid? ReferenceId,
    Guid? VictimTeamId,
    Guid? ActorUserId,
    int Multiplicity,
    long? EarnedPointsPerOccurrence,
    long? DeductedPointsPerOccurrence);

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
    IReadOnlyList<ScoreboardSlotEntry> Entries)
{
    public ScoreboardOperationState OffenseState { get; init; }
    public ScoreboardOperationState DefenseState { get; init; }
}

public sealed record ScoreboardChallengeScore(
    Guid CompetitionChallengeId,
    long AttackScore,
    long DefenseScore);

public sealed record ScoreboardTeam(
    Guid TeamId,
    string TeamName,
    string TrackKey,
    int? Rank,
    ScoreboardRankingState RankingState,
    long TotalScore,
    int GlobalAdjustmentCount,
    IReadOnlyList<ScoreboardAdjustment> GlobalAdjustments,
    IReadOnlyList<ScoreboardSlot> Slots)
{
    public long ScoreOutsideWindow { get; init; }
    public long? AttackScore { get; init; }
    public long? DefenseScore { get; init; }
    public IReadOnlyList<ScoreboardChallengeScore> ChallengeScores { get; init; } = [];
}

public sealed record ScoreboardTrack(
    string Key,
    string Name,
    bool IsInternal,
    bool VisibleOnLeaderboard,
    bool IsViewerTrack = false);

public sealed record ScoreboardCurrentChallengeScore(
    Guid CompetitionChallengeId,
    long? Score,
    long? BreakScore,
    long? FixScore);

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
    public IReadOnlyList<ScoreboardCurrentChallengeScore> CurrentChallengeScores { get; init; } = [];
    public CompetitionLeaderboardVisibility Visibility { get; init; }
    public LeaderboardDataScope DataScope { get; init; }
    public DateTimeOffset? DataAsOf { get; init; }
}

public sealed record ScoreboardProjection(
    ScoreboardChallengeCatalog ChallengeCatalog,
    ScoreboardSchema Schema,
    ScoreboardSnapshot Snapshot)
{
    // These bounded scoring identities let the PostgreSQL detail reader attribute points
    // without embedding raw history in the main snapshot.
    public IReadOnlyList<ScoreboardActor> DetailActors { get; init; } = [];
    public IReadOnlyList<ScoreboardEntryAllocation> EntryAllocations { get; init; } = [];
    public ScoreboardAudienceView? ParticipantView { get; init; }
}

public sealed record ScoreboardAudienceView(
    ScoreboardChallengeCatalog ChallengeCatalog,
    ScoreboardSchema Schema,
    ScoreboardSnapshot Snapshot)
{
    public IReadOnlyList<ScoreboardActor> DetailActors { get; init; } = [];
    public IReadOnlyList<ScoreboardEntryAllocation> EntryAllocations { get; init; } = [];

    public static ScoreboardAudienceView From(ScoreboardProjection projection) => new(
        projection.ChallengeCatalog,
        projection.Schema,
        projection.Snapshot)
    {
        DetailActors = projection.DetailActors,
        EntryAllocations = projection.EntryAllocations
    };

    public ScoreboardProjection ToProjection() => new(ChallengeCatalog, Schema, Snapshot)
    {
        DetailActors = DetailActors,
        EntryAllocations = EntryAllocations
    };
}
