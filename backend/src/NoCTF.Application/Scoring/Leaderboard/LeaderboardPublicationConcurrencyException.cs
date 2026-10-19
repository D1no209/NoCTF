namespace NoCTF.Application.Scoring.Leaderboard;

public enum LeaderboardPublicationStage { BeginBuild, Invalidation, Publication }

public sealed class LeaderboardPublicationConcurrencyException(LeaderboardPublicationStage stage)
    : Exception($"Leaderboard {stage} exceeded the optimistic concurrency retry limit.")
{
    public LeaderboardPublicationStage Stage { get; } = stage;
}
