using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Leaderboard;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class CtfCompletionEligibilityTests
{
    [Test]
    public async Task Query_preselection_and_in_memory_membership_use_the_same_rules()
    {
        var predicate = CtfCompletionEligibility.ParticipatingTeams.Compile();
        foreach (var status in Enum.GetValues<TeamRegistrationStatus>())
        foreach (var banned in new[] { false, true })
        foreach (var deleted in new[] { false, true })
        {
            var team = new Team { RegistrationStatus = status, IsBanned = banned, DeletedAt = deleted ? DateTimeOffset.UnixEpoch : null };
            await Assert.That(predicate(team)).IsEqualTo(CtfCompletionEligibility.CanParticipate(status, banned, deleted));
        }
    }

    [Test, Arguments(CtfInteractionKind.FlagSubmission), Arguments(CtfInteractionKind.PatchVerification)]
    public async Task Public_blood_ranks_follow_interaction_and_ignore_non_blood_tracks_without_changing_points(CtfInteractionKind interaction)
    {
        var at = DateTimeOffset.Parse("2026-09-18T00:00:00Z");
        var challenge = Guid.NewGuid();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var guest = Guid.NewGuid();
        var kind = CtfCompletionEligibility.CompletionKind(interaction);
        LeaderboardGameplayFact Fact(Guid team, int seconds) => new(Guid.NewGuid(), team, challenge, kind,
            at.AddSeconds(seconds), GameplayFactState.Completed, GameplayFactResult.Correct, null);
        var input = new LeaderboardProjectionInput(Guid.NewGuid(), GameMode.Ctf,
            [new(first, "first", false, false), new(second, "second", false, false), new(guest, "guest", false, false, EarnsBlood: false)],
            [Fact(guest, 1), Fact(first, 2), Fact(first, 3), Fact(second, 4)],
            [new(challenge, "Web", "test", false, InteractionKind: interaction)], ProjectedAt: at.AddMinutes(1));
        var expected = new CtfLeaderboardProjector().Project(input);
        var result = new LeaderboardProjectionEngine(new LeaderboardProjectorCatalog()).Project(input);
        await Assert.That(result.Snapshot.Teams.Single(team => team.TeamId == first).Slots
            .SelectMany(slot => slot.Entries)
            .Any(entry => entry.Award == ScoreboardAward.FirstBlood)).IsTrue();
        await Assert.That(result.Snapshot.Teams.Single(team => team.TeamId == second).Slots
            .SelectMany(slot => slot.Entries)
            .Any(entry => entry.Award == ScoreboardAward.SecondBlood)).IsTrue();
        await Assert.That(result.Snapshot.Teams.Single(team => team.TeamId == guest).Slots
            .SelectMany(slot => slot.Entries)
            .All(entry => entry.Award is null)).IsTrue();
        await Assert.That(result.Snapshot.Teams.Select(team => (team.TeamId, team.TotalScore)))
            .IsEquivalentTo(expected.Entries.Select(team => (team.TeamId, TotalScore: team.Score)));
    }
}
