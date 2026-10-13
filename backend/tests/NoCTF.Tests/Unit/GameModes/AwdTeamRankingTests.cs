using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Awd;
using NoCTF.GameModes.Awd.Scoring;
using NoCTF.GameModes.Leaderboard;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class AwdTeamRankingTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-10-13T00:00:00Z");

    [Test]
    public async Task Default_and_custom_rules_keep_total_first_and_team_identity_last()
    {
        var a = Guid.Parse("00000001-0000-0000-0000-000000000000");
        var b = Guid.Parse("00000002-0000-0000-0000-000000000000");
        AwdRankingValues[] rows = [new(a, 100, 60, 1, 1, At, At), new(b, 100, 50, 9, 4, At, At)];
        var input = new LeaderboardProjectionInput(Guid.NewGuid(), GameMode.Awd, [], []);
        await Assert.That(AwdTeamRanking.Order(rows, input, value => value).First().TeamId).IsEqualTo(a);
        var custom = input with { AwdTieBreakers = [new(AwdTieBreakField.HealthyRounds, AwdSortDirection.Descending)] };
        await Assert.That(AwdTeamRanking.Order(rows, custom, value => value).First().TeamId).IsEqualTo(b);
        await Assert.That(AwdTeamRanking.Order([rows[0] with { TotalScore = 101 }, rows[1]], custom, value => value).First().TeamId).IsEqualTo(a);
        await Assert.That(AwdTeamRanking.Order([rows[1] with { HealthyRounds = 1 }, rows[0]], custom, value => value).First().TeamId).IsEqualTo(a);
    }

    [Test]
    [Arguments(AwdSortDirection.Ascending)]
    [Arguments(AwdSortDirection.Descending)]
    public async Task Missing_attack_timestamp_is_never_invented_to_win_a_tie(AwdSortDirection direction)
    {
        var present = new AwdRankingValues(Guid.NewGuid(), 0, 0, 0, 0, At, At);
        var missing = present with { TeamId = Guid.NewGuid(), LastAttackAt = null };
        var input = new LeaderboardProjectionInput(Guid.NewGuid(), GameMode.Awd, [], [],
            AwdTieBreakers: [new(AwdTieBreakField.LastAttackAt, direction)]);
        await Assert.That(AwdTeamRanking.Order([missing, present], input, value => value).First().TeamId).IsEqualTo(present.TeamId);
    }

    [Test]
    public async Task Round_policy_governs_final_ranking_after_writeup_deductions()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var challenge = Guid.NewGuid();
        var configuration = (AwdCompetitionModeConfiguration)CompetitionModeConfigurationDefaults.Create(GameMode.Awd, Guid.NewGuid());
        configuration.TieBreakers = [new() { Field = AwdTieBreakField.HealthyRounds, Direction = AwdSortDirection.Descending }];
        var input = new LeaderboardProjectionInput(configuration.CompetitionId, GameMode.Awd,
            [new(a, "A", false, false), new(b, "B", false, false)], [], [new(challenge, "Web", "Service", false)],
            configuration, At, ProjectedAt: At.AddHours(1), CompetitionStatus: CompetitionStatus.Running,
            AwdAggregates: [new(a, challenge, 200, 100, 1, 10, At, 200), new(b, challenge, 100, 60, 1, 1, At, 100)],
            WriteUpUnlocks: [new(Guid.NewGuid(), a, challenge, 50, At)], AwdTieBreakers: AwdTieBreakPolicy.Default);
        var engine = new LeaderboardProjectionEngine(new LeaderboardProjectorCatalog());
        var result = engine.Project(input);
        await Assert.That(result.Snapshot.Teams.All(team => team.TotalScore == 100)).IsTrue();
        await Assert.That(result.Snapshot.Teams.Single(team => team.Rank == 1).TeamId).IsEqualTo(b);
        var next = engine.Project(input with { AwdTieBreakers = [new(AwdTieBreakField.HealthyRounds, AwdSortDirection.Descending)] });
        await Assert.That(next.Snapshot.Teams.Single(team => team.Rank == 1).TeamId).IsEqualTo(a);
    }

    [Test]
    public async Task Duplicate_unknown_and_empty_custom_contracts_are_rejected()
    {
        await Assert.That(AwdTieBreakPolicy.IsValid([])).IsFalse();
        await Assert.That(AwdTieBreakPolicy.IsValid([new(AwdTieBreakField.AttackCount, AwdSortDirection.Ascending),
            new(AwdTieBreakField.AttackCount, AwdSortDirection.Descending)])).IsFalse();
        await Assert.That(AwdTieBreakPolicy.IsValid([new((AwdTieBreakField)99, AwdSortDirection.Ascending)])).IsFalse();
        await Assert.That(AwdTieBreakPolicy.IsValid([new(AwdTieBreakField.AttackPoints, (AwdSortDirection)99)])).IsFalse();
        await Assert.That(AwdTieBreakPolicy.IsValid(AwdTieBreakPolicy.Default)).IsTrue();
    }
}
