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

public sealed record LeaderboardSlotSummary(
    string SlotKey,
    LeaderboardSlotKind Kind,
    string Label,
    int SuccessCount,
    DateTimeOffset? LastOccurredAt,
    DateTimeOffset? FirstBloodAt);

public sealed record LeaderboardSubjectSummary(
    Guid SubjectId,
    string SubjectName,
    long Score,
    int SuccessCount,
    IReadOnlyList<LeaderboardSlotSummary> Slots);

public sealed record LeaderboardFirstBloodSummary(
    string SlotKey,
    LeaderboardSlotKind SlotKind,
    Guid TeamId,
    string TeamName,
    DateTimeOffset OccurredAt);

public sealed record LeaderboardEntry(
    int Rank,
    Guid TeamId,
    string TeamName,
    long Score,
    int SolveCount,
    DateTimeOffset? LastScoreAt,
    IReadOnlyList<LeaderboardChallengeSummary> Challenges);
