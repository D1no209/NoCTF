using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Submissions;
using NoCTF.GameModes.Leaderboard;

namespace NoCTF.Tests.Unit.GameModes;

public class LeaderboardProjectionEngineTests
{
    [Test]
    public async Task CtfSummary_UsesFirstCorrectFactForFirstBlood()
    {
        var firstTeam = Guid.NewGuid();
        var secondTeam = Guid.NewGuid();
        var challenge = Guid.NewGuid();
        var input = new LeaderboardProjectionInput(
            Guid.NewGuid(),
            GameMode.Ctf,
            [new(firstTeam, "first", false, false), new(secondTeam, "second", false, false)],
            [
                Fact(firstTeam, challenge, 0, ScoringResult.Wrong),
                Fact(secondTeam, challenge, 1, ScoringResult.Correct),
                Fact(firstTeam, challenge, 2, ScoringResult.Correct)
            ],
            [],
            [new(challenge, "web", false)]);

        var result = Engine().Project(input);

        await Assert.That(result.FirstBloods).HasSingleItem();
        await Assert.That(result.FirstBloods[0].TeamId).IsEqualTo(secondTeam);
        await Assert.That(result.FirstBloods[0].SlotKind).IsEqualTo(LeaderboardSlotKind.Challenge);
        var second = result.Subjects.Single(subject => subject.SubjectId == secondTeam);
        await Assert.That(second.Slots).HasSingleItem();
        await Assert.That(second.Slots[0].Label).IsEqualTo("web");
        await Assert.That(second.Slots[0].FirstBloodAt).IsEqualTo(DateTimeOffset.UnixEpoch.AddSeconds(1));
        var first = result.Subjects.Single(subject => subject.SubjectId == firstTeam);
        await Assert.That(first.Slots[0].FirstBloodAt).IsNull();
    }

    [Test]
    [Arguments(GameMode.Awd, LeaderboardSlotKind.Service)]
    [Arguments(GameMode.Awdp, LeaderboardSlotKind.Fix)]
    [Arguments(GameMode.Koh, LeaderboardSlotKind.Control)]
    [Arguments(GameMode.Penetration, LeaderboardSlotKind.Stage)]
    public async Task Summary_UsesModeSpecificSlotKind(GameMode mode, LeaderboardSlotKind expectedKind)
    {
        var team = Guid.NewGuid();
        var challenge = Guid.NewGuid();
        var submission = mode switch
        {
            GameMode.Awd => Fact(team, challenge, 1, ScoringResult.Correct) with { ServiceId = Guid.NewGuid() },
            GameMode.Awdp => Fact(team, challenge, 1, ScoringResult.Correct) with { Kind = SubmissionKind.Fix },
            GameMode.Penetration => Fact(team, challenge, 1, ScoringResult.Correct) with { StageId = Guid.NewGuid() },
            _ => null
        };
        var system = mode == GameMode.Koh
            ? new LeaderboardSystemFact(new ScoringEvent
            {
                Id = Guid.NewGuid(),
                TeamId = team,
                ChallengeId = challenge,
                Kind = ScoringEventKind.KohObservation,
                Result = ScoringResult.Correct,
                OccurredAt = DateTimeOffset.UnixEpoch.AddSeconds(1)
            })
            : null;
        var input = new LeaderboardProjectionInput(
            Guid.NewGuid(),
            mode,
            [new(team, "team", false, false)],
            submission is null ? [] : [submission],
            system is null ? [] : [system],
            [new(challenge, "slot", false)]);

        var result = Engine().Project(input);

        await Assert.That(result.Subjects).HasSingleItem();
        await Assert.That(result.Subjects[0].Slots).HasSingleItem();
        await Assert.That(result.Subjects[0].Slots[0].Kind).IsEqualTo(expectedKind);
    }

    private static LeaderboardProjectionEngine Engine() => new(new LeaderboardProjectorCatalog());

    private static LeaderboardSubmissionFact Fact(
        Guid teamId,
        Guid challengeId,
        int seconds,
        ScoringResult result) =>
        new(
            Guid.CreateVersion7(DateTimeOffset.UnixEpoch.AddSeconds(seconds)),
            teamId,
            challengeId,
            SubmissionKind.Flag,
            DateTimeOffset.UnixEpoch.AddSeconds(seconds),
            new ScoringEvent
            {
                Id = Guid.CreateVersion7(DateTimeOffset.UnixEpoch.AddSeconds(seconds)),
                TeamId = teamId,
                ChallengeId = challengeId,
                Result = result,
                OccurredAt = DateTimeOffset.UnixEpoch.AddSeconds(seconds)
            });
}
