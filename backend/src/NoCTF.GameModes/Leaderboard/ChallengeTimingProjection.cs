using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Gameplay;

namespace NoCTF.GameModes.Leaderboard;

internal static class ChallengeTimingProjection
{
    public static bool CanScore(LeaderboardProjectionInput input, LeaderboardGameplayFact fact)
    {
        if (fact.Kind is GameplayFactKind.HintUnlock or GameplayFactKind.ManualAdjustment or GameplayFactKind.AwdServiceTransition)
            return true;
        var timing = input.Challenges?.FirstOrDefault(x => x.Id == fact.CompetitionChallengeId)?.Timing;
        if (timing is null) return true;
        return fact.Kind == GameplayFactKind.KohControlObservation
            ? timing.CanScoreCheckpoint(fact.OccurredAt) : timing.CanScore(fact.OccurredAt);
    }

    public static LeaderboardProjectionInput ForScoring(LeaderboardProjectionInput input) => input with
    {
        GameplayFacts = input.GameplayFacts.Where(fact => CanScore(input, fact)).Select(fact =>
            fact.Result == GameplayFactResult.RightButDue ? fact with { Result = GameplayFactResult.Correct } : fact).ToArray(),
        AwdRounds = input.AwdRounds?.Where(round => CanScoreInterval(input, round.CompetitionChallengeId, round.StartsAt, round.EndsAt)).ToArray()
    };

    public static LeaderboardProjectionInput WithCurrentResults(LeaderboardProjectionInput input)
    {
        LeaderboardGameplayFact Current(LeaderboardGameplayFact fact)
        {
            var timing = input.Challenges?.FirstOrDefault(x => x.Id == fact.CompetitionChallengeId)?.Timing;
            return timing is not null && fact.Result is { } result && fact.Kind is GameplayFactKind.FlagAttempt or GameplayFactKind.BreakAttempt or GameplayFactKind.FixAttempt
                ? fact with { Result = timing.Classify(result, fact.OccurredAt) } : fact;
        }
        return input with { GameplayFacts = input.GameplayFacts.Select(Current).ToArray(),
            ScoreboardGameplayFacts = input.ScoreboardGameplayFacts?.Select(Current).ToArray() };
    }

    public static bool CanScoreInterval(LeaderboardProjectionInput input, Guid challengeId, DateTimeOffset start, DateTimeOffset end) =>
        input.Challenges?.FirstOrDefault(x => x.Id == challengeId)?.Timing?.CanScoreInterval(start, end) ?? true;
}
