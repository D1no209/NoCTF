using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.GameModes.Leaderboard;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class ChallengeTimingScoringTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);
    [Test]
    public async Task Ctf_latest_end_removes_success_and_wrong_penalties_but_preserves_manual_adjustment()
    {
        var team = Guid.NewGuid(); var challenge = Guid.NewGuid();
        var facts = new[] {
            Fact(team, challenge, GameplayFactKind.FlagAttempt, GameplayFactResult.Correct, 90),
            Fact(team, challenge, GameplayFactKind.FlagAttempt, GameplayFactResult.Wrong, 91),
            Fact(team, challenge, GameplayFactKind.ManualAdjustment, GameplayFactResult.Applied, 92) with { Value = "25" }
        };
        var input = Input(GameMode.Ctf, team, challenge, facts, new(null, Start.AddSeconds(60)));
        var result = new LeaderboardProjectionEngine(new LeaderboardProjectorCatalog()).Project(input);
        await Assert.That(result.Snapshot.Teams.Single().TotalScore).IsEqualTo(25);
        var restored = new CtfLeaderboardProjector().Project(input with {
            Challenges = [new(challenge, "Web", "Question", false, Timing: new(null, Start.AddSeconds(120)))]
        });
        await Assert.That(restored.Entries.Single().SolveCount).IsEqualTo(1);
        await Assert.That(restored.Entries.Single().Score).IsGreaterThan(25);
    }
    [Test]
    public async Task Koh_checkpoint_at_scoring_end_has_no_points_or_competitive_control_count()
    {
        var team = Guid.NewGuid(); var challenge = Guid.NewGuid();
        var input = Input(GameMode.Koh, team, challenge,
            [Fact(team, challenge, GameplayFactKind.KohControlObservation, GameplayFactResult.Controlled, 59),
             Fact(team, challenge, GameplayFactKind.KohControlObservation, GameplayFactResult.Controlled, 60)],
            new(null, Start.AddSeconds(60)));
        var projection = new LeaderboardProjectionEngine(new LeaderboardProjectorCatalog()).Project(input);
        var direct = new KohLeaderboardProjector().Project(input);
        await Assert.That(direct.Entries.Single().SolveCount).IsEqualTo(1);
        await Assert.That(projection.Snapshot.Teams.Single().TotalScore).IsEqualTo(direct.Entries.Single().Score);
    }
    [Test]
    [Arguments(GameMode.Awd)]
    [Arguments(GameMode.Awdp)]
    public async Task Late_attack_has_no_attacker_gain_or_victim_loss(GameMode mode)
    {
        var team = Guid.NewGuid(); var victim = Guid.NewGuid(); var challenge = Guid.NewGuid();
        var kind = mode == GameMode.Awd ? GameplayFactKind.FlagAttempt : GameplayFactKind.BreakAttempt;
        var fact = Fact(team, challenge, kind, GameplayFactResult.Correct, 90) with {
            VictimTeamId = victim, ReferenceKind = GameplayFactReferenceKind.AwdRound, ReferenceId = Guid.NewGuid()
        };
        var input = Input(mode, team, challenge, [fact], new(null, Start.AddSeconds(60))) with {
            Teams = [new(team, "Attacker", false, false), new(victim, "Victim", false, false)]
        };
        var result = new LeaderboardProjectionEngine(new LeaderboardProjectorCatalog()).Project(input);
        await Assert.That(result.Snapshot.Teams.All(x => x.TotalScore == 0)).IsTrue();
    }
    private static LeaderboardGameplayFact Fact(Guid team, Guid challenge, GameplayFactKind kind, GameplayFactResult result, int seconds) =>
        new(Guid.NewGuid(), team, challenge, kind, Start.AddSeconds(seconds), GameplayFactState.Completed, result, null);
    private static LeaderboardProjectionInput Input(GameMode mode, Guid team, Guid challenge, IReadOnlyList<LeaderboardGameplayFact> facts,
        ChallengeTiming timing) => new(Guid.NewGuid(), mode, [new(team, "Team", false, false)], facts,
            [new(challenge, "Web", "Question", false, Timing: timing)], CompetitionStartTime: Start,
            ProjectedAt: Start.AddHours(1), CompetitionStatus: CompetitionStatus.Running);
}
