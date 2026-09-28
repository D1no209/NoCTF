using BenchmarkDotNet.Attributes;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Challenges;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.GameModes.Ctf.Configuration;
using NoCTF.GameModes.Leaderboard;
using NoCTF.GameModes.Scoring;
using NoCTF.GameModes.Registration;

namespace NoCTF.Benchmarks;

[MemoryDiagnoser]
public class LeaderboardProjectionBenchmarks
{
    private readonly LeaderboardProjectionEngine engine = new(new LeaderboardProjectorCatalog());
    private LeaderboardProjectionInput input = null!;

    [Params(GameMode.Ctf, GameMode.Awd, GameMode.Awdp, GameMode.Koh)]
    public GameMode Mode { get; set; }

    [Params(16, 64)]
    public int TeamCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        input = Corpus.Create(Mode, TeamCount);
        var projection = engine.Project(input);
        if (projection.Snapshot.Teams.Count == 0
            || Mode != GameMode.Koh && projection.EntryAllocations.Count == 0)
            throw new InvalidOperationException("The benchmark corpus did not exercise the leaderboard hot path.");
    }

    [Benchmark]
    public ScoreboardProjection Project() => engine.Project(input);

    internal static LeaderboardProjectionInput CreateMixedCtfInput(int teamCount) =>
        Corpus.Create(GameMode.Ctf, teamCount, mixedCtf: true);

    private static class Corpus
    {
        private const int ChallengeCount = 12;
        private const int RoundCount = 20;
        private static readonly DateTimeOffset Start =
            DateTimeOffset.Parse("2026-08-28T00:00:00Z");

        public static LeaderboardProjectionInput Create(
            GameMode mode,
            int teamCount,
            bool mixedCtf = false)
        {
            var competitionId = StableGuid(1);
            var challenges = Enumerable.Range(1, ChallengeCount)
                .Select(index => new LeaderboardChallengeFact(
                    StableGuid(10_000 + index),
                    "general",
                    $"Challenge {index}",
                    false,
                    ChallengeConfiguration(mode, index),
                    Order: index,
                    InteractionKind: mode == GameMode.Ctf && mixedCtf && index % 2 == 0
                        ? CtfInteractionKind.PatchVerification
                        : CtfInteractionKind.FlagSubmission))
                .ToArray();
            var teams = Enumerable.Range(1, teamCount)
                .Select(index => new LeaderboardTeamFact(
                    StableGuid(20_000 + index),
                    $"Team {index}",
                    false,
                    false,
                    Start.AddSeconds(index),
                    TrackKey: index % 8 == 0 ? "guest" : CompetitionTrackConfiguration.DefaultTrackKey,
                    EarnsScore: true,
                    EarnsBlood: index % 8 != 0,
                    AffectsDynamicChallengeScore: index % 8 != 0,
                    VisibleOnLeaderboard: true,
                    AffectsCompetitiveResults: index % 8 != 0))
                .ToArray();
            var corpus = mode switch
            {
                GameMode.Ctf => CreateCtf(teams, challenges),
                GameMode.Awd => CreateAwd(teams, challenges),
                GameMode.Awdp => CreateAwdp(teams, challenges),
                GameMode.Koh => CreateKoh(teams, challenges),
                _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
            };
            return new(
                competitionId,
                mode,
                teams,
                corpus.AggregateFacts,
                challenges,
                CompetitionConfiguration(mode),
                CompetitionStartTime: Start,
                LifecycleAudits: Lifecycle(competitionId),
                AwdRounds: corpus.AwdRounds,
                ProjectedAt: Start.AddHours(6),
                CompetitionStatus: CompetitionStatus.Running,
                ScoreboardGameplayFacts: corpus.ScoreboardFacts,
                ScoreboardRoundWindowEnd: mode == GameMode.Awd ? RoundCount : null,
                ScoreboardLatestRound: mode == GameMode.Awd ? RoundCount : null,
                AwdAggregates: corpus.AwdAggregates);
        }

        private static CorpusData CreateCtf(
            IReadOnlyList<LeaderboardTeamFact> teams,
            IReadOnlyList<LeaderboardChallengeFact> challenges)
        {
            var facts = new List<LeaderboardGameplayFact>();
            var sequence = 100_000;
            foreach (var (team, teamIndex) in teams.Select((value, index) => (value, index)))
            {
                foreach (var (challenge, challengeIndex) in challenges.Select((value, index) => (value, index)))
                {
                    var actorId = StableGuid(500_000 + teamIndex * 10 + challengeIndex % 4);
                    var occurredAt = Start.AddMinutes(5 + teamIndex).AddSeconds(challengeIndex);
                    var interactionKind = challenge.InteractionKind == CtfInteractionKind.PatchVerification
                        ? GameplayFactKind.FixAttempt
                        : GameplayFactKind.FlagAttempt;
                    facts.Add(Fact(sequence++, team.Id, challenge.Id, interactionKind,
                        occurredAt, GameplayFactResult.Correct, actorId: actorId));
                    facts.Add(Fact(sequence++, team.Id, challenge.Id, interactionKind,
                        occurredAt.AddSeconds(-1), GameplayFactResult.Wrong, actorId: actorId, multiplicity: 3));
                    if (challengeIndex % 3 == 0)
                        facts.Add(Fact(sequence++, team.Id, challenge.Id, GameplayFactKind.HintUnlock,
                            occurredAt.AddSeconds(-2), GameplayFactResult.Unlocked, actorId: actorId, hintCost: 10));
                }
                facts.Add(Fact(sequence++, team.Id, challenges[0].Id, GameplayFactKind.ManualAdjustment,
                    Start.AddHours(5), GameplayFactResult.Applied, actorId: StableGuid(900_000), value: "-5"));
            }
            return new(facts, facts, null, null);
        }

        private static CorpusData CreateAwd(
            IReadOnlyList<LeaderboardTeamFact> teams,
            IReadOnlyList<LeaderboardChallengeFact> challenges)
        {
            var aggregate = new List<LeaderboardGameplayFact>();
            var scoreboard = new List<LeaderboardGameplayFact>();
            var rounds = new List<LeaderboardAwdRoundFact>();
            var aggregates = new List<LeaderboardAwdAggregateFact>();
            var roundIds = Enumerable.Range(1, RoundCount)
                .Select(index => StableGuid(600_000 + index))
                .ToArray();
            var sequence = 200_000;
            foreach (var (team, teamIndex) in teams.Select((value, index) => (value, index)))
            {
                foreach (var (challenge, challengeIndex) in challenges.Select((value, index) => (value, index)))
                {
                    for (var roundIndex = 0; roundIndex < RoundCount; roundIndex++)
                    {
                        var startsAt = Start.AddMinutes(roundIndex * 5);
                        rounds.Add(new(challenge.Id, team.Id, roundIds[roundIndex], startsAt, startsAt.AddMinutes(5)));
                        if (roundIndex % 4 == 0)
                            scoreboard.Add(Fact(sequence++, team.Id, challenge.Id,
                                GameplayFactKind.AwdServiceTransition,
                                startsAt.AddMinutes(1),
                                (teamIndex + challengeIndex + roundIndex) % 7 == 0
                                    ? GameplayFactResult.ServiceDown
                                    : GameplayFactResult.ServiceUp));
                        if (roundIndex % 5 == 0 && teamIndex % 4 == 0)
                        {
                            var victim = teams[(teamIndex + 1) % teams.Count];
                            scoreboard.Add(Fact(sequence++, team.Id, challenge.Id,
                                GameplayFactKind.FlagAttempt,
                                startsAt.AddMinutes(2),
                                GameplayFactResult.Correct,
                                referenceKind: GameplayFactReferenceKind.AwdRound,
                                referenceId: roundIds[roundIndex],
                                victimTeamId: victim.Id,
                                actorId: StableGuid(700_000 + teamIndex)));
                        }
                    }
                    aggregates.Add(new(
                        team.Id,
                        challenge.Id,
                        1_500 + teamIndex - challengeIndex,
                        200,
                        4,
                        18,
                        Start.AddMinutes(92)));
                }
                var adjustment = Fact(sequence++, team.Id, challenges[0].Id,
                    GameplayFactKind.ManualAdjustment, Start.AddHours(5),
                    GameplayFactResult.Applied, actorId: StableGuid(900_000), value: "-5");
                aggregate.Add(adjustment);
                scoreboard.Add(adjustment);
            }
            return new(aggregate, scoreboard, rounds, aggregates);
        }

        private static CorpusData CreateAwdp(
            IReadOnlyList<LeaderboardTeamFact> teams,
            IReadOnlyList<LeaderboardChallengeFact> challenges)
        {
            var facts = new List<LeaderboardGameplayFact>();
            var sequence = 300_000;
            foreach (var (team, teamIndex) in teams.Select((value, index) => (value, index)))
            {
                foreach (var (challenge, challengeIndex) in challenges.Select((value, index) => (value, index)))
                {
                    var actorId = StableGuid(800_000 + teamIndex * 10 + challengeIndex % 4);
                    var breakAt = Start.AddMinutes(10 + (teamIndex % 40) * 5 + challengeIndex);
                    facts.Add(Fact(sequence++, team.Id, challenge.Id, GameplayFactKind.BreakAttempt,
                        breakAt, GameplayFactResult.Correct, actorId: actorId));
                    facts.Add(Fact(sequence++, team.Id, challenge.Id, GameplayFactKind.FixAttempt,
                        breakAt.AddMinutes(5), GameplayFactResult.Correct, actorId: actorId));
                    facts.Add(Fact(sequence++, team.Id, challenge.Id, GameplayFactKind.BreakAttempt,
                        breakAt.AddMinutes(1), GameplayFactResult.Wrong, actorId: actorId, multiplicity: 3));
                    facts.Add(Fact(sequence++, team.Id, challenge.Id, GameplayFactKind.FixAttempt,
                        breakAt.AddMinutes(2), GameplayFactResult.Rejected,
                        GameplayFactFailureCode.AwdpServiceAbnormal, actorId: actorId, multiplicity: 2));
                }
                facts.Add(Fact(sequence++, team.Id, challenges[0].Id, GameplayFactKind.ManualAdjustment,
                    Start.AddHours(5), GameplayFactResult.Applied, actorId: StableGuid(900_000), value: "-5"));
            }
            return new(facts, facts, null, null);
        }

        private static CorpusData CreateKoh(
            IReadOnlyList<LeaderboardTeamFact> teams,
            IReadOnlyList<LeaderboardChallengeFact> challenges)
        {
            var facts = new List<LeaderboardGameplayFact>();
            var sequence = 400_000;
            foreach (var (team, teamIndex) in teams.Select((value, index) => (value, index)))
            {
                foreach (var (challenge, challengeIndex) in challenges.Select((value, index) => (value, index)))
                    facts.Add(Fact(sequence++, team.Id, challenge.Id, GameplayFactKind.KohControlObservation,
                        Start.AddMinutes(teamIndex + challengeIndex), GameplayFactResult.Controlled,
                        multiplicity: RoundCount,
                        lastOccurredAt: Start.AddMinutes(100 + teamIndex + challengeIndex)));
                facts.Add(Fact(sequence++, team.Id, challenges[0].Id, GameplayFactKind.ManualAdjustment,
                    Start.AddHours(5), GameplayFactResult.Applied, actorId: StableGuid(900_000), value: "-5"));
            }
            return new(facts, facts, null, null);
        }

        private static LeaderboardGameplayFact Fact(
            int sequence,
            Guid? teamId,
            Guid challengeId,
            GameplayFactKind kind,
            DateTimeOffset occurredAt,
            GameplayFactResult result,
            GameplayFactFailureCode? failureCode = null,
            GameplayFactReferenceKind? referenceKind = null,
            Guid? referenceId = null,
            Guid? victimTeamId = null,
            Guid? actorId = null,
            string? value = null,
            long? hintCost = null,
            int multiplicity = 1,
            DateTimeOffset? lastOccurredAt = null) => new(
                StableGuid(sequence),
                teamId,
                challengeId,
                kind,
                occurredAt,
                GameplayFactState.Completed,
                result,
                failureCode,
                referenceKind,
                referenceId,
                victimTeamId,
                actorId is null ? null : $"User {actorId.Value:N}",
                value,
                hintCost,
                actorId,
                multiplicity,
                lastOccurredAt);

        private static CompetitionModeConfiguration CompetitionConfiguration(GameMode mode)
        {
            var configuration = CompetitionModeConfigurationDefaults.Create(mode, Guid.Empty);
            switch (configuration)
            {
                case CtfCompetitionModeConfiguration ctf:
                    ctf.DefaultScoreCurve = Curve(500, 100, 50);
                    ctf.WrongSubmissionPenalty = 2;
                    ctf.BloodRewards =
                    [
                        new CompetitionBloodReward
                        {
                            Policy = CompetitionBloodRewardPolicy.CurrentPointsPercentage,
                            Value = 10
                        }
                    ];
                    break;
                case AwdpCompetitionModeConfiguration awdp:
                    awdp.BreakScoreCurve = Curve(500, 100, 50);
                    awdp.FixScoreCurve = Curve(400, 100, 50);
                    awdp.FlagWrongPenalty = 2;
                    awdp.ExploitSucceededPenalty = 3;
                    awdp.ServiceAbnormalPenalty = 5;
                    break;
                case KohCompetitionModeConfiguration koh:
                    koh.PollIntervalSeconds = 30;
                    koh.ControlPointsPerInterval = 5;
                    break;
            }
            return configuration;
        }

        private static CompetitionChallengeRules ChallengeConfiguration(GameMode mode, int index)
        {
            var rules = new GameModeChallengeConfigurationCatalog()
                .CreateDefaultRules(mode, Guid.Empty);
            switch (rules)
            {
                case CtfCompetitionChallengeRules ctf:
                    ctf.HasScoreCurve = index % 3 == 0;
                    ctf.ScoreCurve = Curve(600, 120, 60);
                    ctf.WrongSubmissionPenalty = index % 4 == 0 ? 3 : null;
                    break;
                case AwdCompetitionChallengeRules awd:
                    awd.ServiceHealthyPoints = index % 3 == 0 ? 110 : null;
                    break;
                case AwdpCompetitionChallengeRules awdp:
                    awdp.RequireBreakBeforeFix = index % 4 != 0;
                    awdp.FlagWrongPenalty = index % 3 == 0 ? 4 : null;
                    break;
                case KohCompetitionChallengeRules koh:
                    koh.ControlPointsPerInterval = 5 + index % 3;
                    break;
            }
            return rules;
        }

        private static ScoreCurveValue Curve(long initial, long minimum, int teams) => new()
        {
            InitialPoints = initial,
            MinimumPoints = minimum,
            DecayTeamCount = teams,
            DecayMode = PersistedScoreDecayMode.Quadratic
        };

        private static IReadOnlyList<CompetitionLifecycleTransition> Lifecycle(Guid competitionId) =>
        [
            new()
            {
                Id = StableGuid(950_001), CompetitionId = competitionId,
                From = CompetitionStatus.Published, To = CompetitionStatus.Running, OccurredAt = Start
            },
            new()
            {
                Id = StableGuid(950_002), CompetitionId = competitionId,
                From = CompetitionStatus.Running, To = CompetitionStatus.Paused, OccurredAt = Start.AddHours(2)
            },
            new()
            {
                Id = StableGuid(950_003), CompetitionId = competitionId,
                From = CompetitionStatus.Paused, To = CompetitionStatus.Running,
                OccurredAt = Start.AddHours(2).AddMinutes(10)
            }
        ];

        private static Guid StableGuid(int value) =>
            new(value, 0, 0, new byte[8]);

        private sealed record CorpusData(
            IReadOnlyList<LeaderboardGameplayFact> AggregateFacts,
            IReadOnlyList<LeaderboardGameplayFact> ScoreboardFacts,
            IReadOnlyList<LeaderboardAwdRoundFact>? AwdRounds,
            IReadOnlyList<LeaderboardAwdAggregateFact>? AwdAggregates);
    }
}

[MemoryDiagnoser]
public class CtfMixedLeaderboardProjectionBenchmarks
{
    private readonly LeaderboardProjectionEngine engine = new(new LeaderboardProjectorCatalog());
    private LeaderboardProjectionInput input = null!;

    [Params(16, 64)]
    public int TeamCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        input = LeaderboardProjectionBenchmarks.CreateMixedCtfInput(TeamCount);
        var output = engine.Project(input);
        if (output.Snapshot.Teams.Count == 0 || output.EntryAllocations.Count == 0)
            throw new InvalidOperationException("The mixed CTF corpus did not exercise the leaderboard hot path.");
    }

    [Benchmark]
    public ScoreboardProjection Project() => engine.Project(input);
}
