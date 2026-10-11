using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.GameModes.Leaderboard;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class TheoryScoreProjectionTests
{
    [Test]
    [Arguments(GameMode.Ctf)]
    [Arguments(GameMode.Awd)]
    [Arguments(GameMode.Awdp)]
    public async Task Theory_only_teams_receive_real_score_and_rank_without_practical_achievements(GameMode mode)
    {
        var now = DateTimeOffset.UtcNow;
        var first = Guid.NewGuid(); var second = Guid.NewGuid(); var banned = Guid.NewGuid();
        var input = new LeaderboardProjectionInput(Guid.NewGuid(), mode,
            [new(first, "first", false, false), new(second, "second", false, false), new(banned, "banned", true, false)], [],
            CompetitionConfiguration: TestConfigurations.Competition(mode), CompetitionStartTime: now.AddHours(-1),
            ProjectedAt: now, CompetitionStatus: CompetitionStatus.Running,
            TheoryScores: [new(first, 10, now.AddMinutes(-2), true), new(second, 80, now.AddMinutes(-1), true), new(banned, 1000, now, true)]);
        var board = new LeaderboardProjectionEngine(new LeaderboardProjectorCatalog()).Project(input);
        await Assert.That(board.Snapshot.TheoryEnabled).IsTrue();
        var winner = board.Snapshot.Teams.Single(team => team.TeamId == second);
        await Assert.That(winner.Rank).IsEqualTo(1);
        await Assert.That(winner.TotalScore).IsEqualTo(80L);
        await Assert.That(winner.TheoryScore).IsEqualTo(80L);
        await Assert.That(winner.TheoryScore + winner.ScoreOutsideWindow + winner.Slots.Sum(slot => slot.NetPoints ?? 0)
            + winner.GlobalAdjustments.Sum(adjustment => adjustment.NetPoints)).IsEqualTo(winner.TotalScore);
        await Assert.That(winner.Achievements ?? []).IsEmpty();
        await Assert.That(board.Snapshot.Teams.Single(team => team.TeamId == banned).TheoryScore).IsEqualTo(0L);
    }

    [Test]
    public async Task Theory_ties_use_achievement_time_and_no_theory_configuration_keeps_mode_projection_unchanged()
    {
        var now = DateTimeOffset.UtcNow;
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        var input = new LeaderboardProjectionInput(Guid.NewGuid(), GameMode.Ctf,
            [new(first, "first", false, false), new(second, "second", false, false)], [],
            CompetitionConfiguration: TestConfigurations.Competition(GameMode.Ctf), ProjectedAt: now,
            TheoryScores: [new(first, 10, now.AddMinutes(-1), true), new(second, 10, now.AddMinutes(-2), true)]);
        var engine = new LeaderboardProjectionEngine(new LeaderboardProjectorCatalog());
        await Assert.That(engine.Project(input).Snapshot.Teams.Single(team => team.TeamId == second).Rank).IsEqualTo(1);
        var disabled = engine.Project(input with { TheoryScores = null });
        await Assert.That(disabled.Snapshot.TheoryEnabled).IsFalse();
        await Assert.That(disabled.Snapshot.Teams.All(team => team.TotalScore == 0)).IsTrue();
    }
}
