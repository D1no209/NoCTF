using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.GameModes.Leaderboard;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class BannedTeamScoreboardTests
{
    [Test, Arguments(CtfInteractionKind.FlagSubmission), Arguments(CtfInteractionKind.PatchVerification)]
    public async Task Ban_excludes_completion_activity_and_achievements_and_unban_restores_them(CtfInteractionKind interaction)
    {
        var now = DateTimeOffset.Parse("2026-10-26T00:00:00Z");
        var challenge = Guid.NewGuid();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var kind = CtfCompletionEligibility.CompletionKind(interaction);
        LeaderboardGameplayFact Fact(Guid team, int seconds) => new(Guid.NewGuid(), team, challenge, kind,
            now.AddSeconds(seconds), GameplayFactState.Completed, GameplayFactResult.Correct, null);
        var facts = new[] { Fact(first, 1), Fact(first, 2), Fact(second, 3) };
        var input = new LeaderboardProjectionInput(Guid.NewGuid(), GameMode.Ctf,
            [new(first, "first", false, false), new(second, "second", false, false)], facts,
            [new(challenge, "Web", "challenge", false, InteractionKind: interaction)],
            CompetitionConfiguration: new CtfCompetitionModeConfiguration
            {
                DefaultScoreCurve = new() { InitialPoints = 500, MinimumPoints = 100, DecayTeamCount = 5, DecayMode = PersistedScoreDecayMode.Linear },
                BloodRewards = []
            },
            ProjectedAt: now.AddMinutes(1), CompetitionStatus: CompetitionStatus.Running,
            ScoreboardGameplayFacts: facts);
        var engine = new LeaderboardProjectionEngine(new LeaderboardProjectorCatalog());
        var before = engine.Project(input);
        await Assert.That(SolverCount(before)).IsEqualTo(2);
        var banned = engine.Project(input with { Teams = [input.Teams[0] with { IsBanned = true }, input.Teams[1]] });
        var row = banned.Snapshot.Teams.Single(team => team.TeamId == first);
        await Assert.That(row.RankingState).IsEqualTo(ScoreboardRankingState.Banned);
        await Assert.That(row.TotalScore).IsEqualTo(0L);
        await Assert.That(row.Slots).IsEmpty();
        await Assert.That(row.Achievements).IsEmpty();
        await Assert.That(SolverCount(banned)).IsEqualTo(1);
        await Assert.That(banned.Snapshot.CurrentChallengeScores.Single().Score).IsEqualTo(500L);
        await Assert.That(banned.BloodAwards.All(award => award.TeamId != first)).IsTrue();
        var restored = engine.Project(input);
        await Assert.That(SolverCount(restored)).IsEqualTo(2);
        await Assert.That(restored.Snapshot.Teams.Single(team => team.TeamId == first).Achievements).Count().IsEqualTo(1);
        await Assert.That(restored.Snapshot.CurrentChallengeScores.Single().Score).IsEqualTo(before.Snapshot.CurrentChallengeScores.Single().Score);
        await Assert.That(input.GameplayFacts.Count).IsEqualTo(3);
    }

    private static int SolverCount(ScoreboardProjection projection) => projection.Snapshot.Teams.Count(team =>
        team.Slots.Any(slot => slot.Breakdowns.Any(activity => activity.Kind == ScoreboardBreakdownKind.Solve && activity.SuccessfulCount > 0)));
}
