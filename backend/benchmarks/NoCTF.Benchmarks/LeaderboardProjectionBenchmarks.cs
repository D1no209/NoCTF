using BenchmarkDotNet.Attributes;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.GameModes.Leaderboard;

namespace NoCTF.Benchmarks;

[MemoryDiagnoser]
public class LeaderboardProjectionBenchmarks
{
    private readonly LeaderboardProjectionEngine engine = new(new LeaderboardProjectorCatalog());
    private LeaderboardProjectionInput input = null!;

    [Params(GameMode.Ctf, GameMode.Awd, GameMode.Awdp, GameMode.Koh)]
    public GameMode Mode { get; set; }

    [Params(8, 64, 256)]
    public int TeamCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        input = Corpus.Create(Mode, TeamCount);
        var separateLegacy = engine.ProjectOutputs(input).Legacy;
        var separateScoreboard = engine.ProjectOutputs(input).Scoreboard;
        var combined = engine.ProjectOutputs(input);
        if (!EqualsByValue(separateLegacy, combined.Legacy)
            || !EqualsByValue(separateScoreboard, combined.Scoreboard))
            throw new InvalidOperationException("Combined projection changed output semantics.");
    }

    [Benchmark(Baseline = true)]
    public (LeaderboardProjectionResult Legacy, ScoreboardProjection Scoreboard) Separate() =>
        (engine.ProjectOutputs(input).Legacy, engine.ProjectOutputs(input).Scoreboard);

    [Benchmark]
    public LeaderboardProjectionOutputs Combined() => engine.ProjectOutputs(input);

    private static bool EqualsByValue<T>(T left, T right) =>
        System.Text.Json.JsonSerializer.Serialize(left)
        == System.Text.Json.JsonSerializer.Serialize(right);

    private static class Corpus
    {
        private static readonly DateTimeOffset Start =
            DateTimeOffset.Parse("2026-08-28T00:00:00Z");

        public static LeaderboardProjectionInput Create(GameMode mode, int teamCount)
        {
            var competitionId = StableGuid(1);
            var challenges = Enumerable.Range(1, 12)
                .Select(index => new LeaderboardChallengeFact(
                    StableGuid(10_000 + index),
                    "general",
                    $"Challenge {index}",
                    false,
                    Order: index))
                .ToArray();
            var teams = Enumerable.Range(1, teamCount)
                .Select(index => new LeaderboardTeamFact(
                    StableGuid(20_000 + index),
                    $"Team {index}",
                    false,
                    false,
                    Start.AddSeconds(index)))
                .ToArray();
            var facts = teams.SelectMany((team, teamIndex) => challenges.Select((challenge, challengeIndex) =>
                Fact(mode, team, challenge, teamIndex, challengeIndex))).ToArray();
            return new(
                competitionId,
                mode,
                teams,
                facts,
                challenges,
                CompetitionStartTime: Start,
                ProjectedAt: Start.AddHours(2),
                CompetitionStatus: CompetitionStatus.Running);
        }

        private static LeaderboardGameplayFact Fact(
            GameMode mode,
            LeaderboardTeamFact team,
            LeaderboardChallengeFact challenge,
            int teamIndex,
            int challengeIndex)
        {
            var kind = mode switch
            {
                GameMode.Awdp => GameplayFactKind.BreakAttempt,
                GameMode.Koh => GameplayFactKind.KohControlObservation,
                _ => GameplayFactKind.FlagAttempt
            };
            var result = mode == GameMode.Koh
                ? GameplayFactResult.Controlled
                : GameplayFactResult.Correct;
            var sequence = 100_000 + teamIndex * 100 + challengeIndex;
            return new(
                StableGuid(sequence),
                team.Id,
                challenge.Id,
                kind,
                Start.AddSeconds(sequence),
                GameplayFactState.Completed,
                result,
                null);
        }

        private static Guid StableGuid(int value) =>
            new(value, 0, 0, new byte[8]);
    }
}
