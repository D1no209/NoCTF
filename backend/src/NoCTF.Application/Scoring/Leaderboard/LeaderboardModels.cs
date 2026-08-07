namespace NoCTF.Application.Scoring.Leaderboard;

public sealed record LeaderboardChallengeSummary(Guid CompetitionChallengeId, string Direction, int SolveCount);

public enum LeaderboardSlotKind
{
    Challenge,
    Service,
    Break,
    Fix,
    Control,
    Stage
}

public enum LeaderboardBloodRank
{
    First = 1,
    Second = 2,
    Third = 3
}

public sealed record LeaderboardSlotSummary(
    string SlotKey,
    LeaderboardSlotKind Kind,
    string Label,
    int SuccessCount,
    DateTimeOffset? LastOccurredAt,
    LeaderboardBloodRank? BloodRank,
    DateTimeOffset? BloodAt);

public sealed record LeaderboardSubjectSummary(
    Guid SubjectId,
    string SubjectName,
    long Score,
    int SuccessCount,
    IReadOnlyList<LeaderboardSlotSummary> Slots);

public sealed record LeaderboardBloodSummary(
    string SlotKey,
    LeaderboardSlotKind SlotKind,
    LeaderboardBloodRank BloodRank,
    Guid TeamId,
    string TeamName,
    DateTimeOffset OccurredAt);

public sealed record LeaderboardScorePoint(DateTimeOffset At, long Score);

public enum LeaderboardPenaltyKind
{
    WrongSubmission,
    HintUnlock
}

public sealed record LeaderboardSolveRecord(
    Guid CompetitionChallengeId,
    DateTimeOffset At,
    long Points,
    int SolveOrdinal,
    string? SubmitterName);

public sealed record LeaderboardPenaltyRecord(
    DateTimeOffset At,
    long Points,
    LeaderboardPenaltyKind Kind);

public sealed record LeaderboardTeamSeries(
    Guid TeamId,
    string TeamName,
    IReadOnlyList<LeaderboardScorePoint> Points)
{
    public IReadOnlyList<LeaderboardSolveRecord> Solves { get; init; } = [];
    public IReadOnlyList<LeaderboardPenaltyRecord> Penalties { get; init; } = [];
}

public sealed record LeaderboardChallengeInfo(
    Guid CompetitionChallengeId,
    string Title,
    string Direction);

public sealed record LeaderboardEntry(
    int Rank,
    Guid TeamId,
    string TeamName,
    long Score,
    int SolveCount,
    DateTimeOffset? LastScoreAt,
    IReadOnlyList<LeaderboardChallengeSummary> Challenges);
