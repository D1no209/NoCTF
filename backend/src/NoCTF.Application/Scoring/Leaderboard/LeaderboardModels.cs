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

public sealed record LeaderboardEntry(
    int Rank,
    Guid TeamId,
    string TeamName,
    long Score,
    int SolveCount,
    DateTimeOffset? LastScoreAt,
    IReadOnlyList<LeaderboardChallengeSummary> Challenges);

public interface ICompetitionLeaderboardAccess
{
    Task<bool> CanReadPrivateAsync(
        Guid userId,
        Guid competitionId,
        CancellationToken cancellationToken);
}
