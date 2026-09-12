using FluentStorage.Storage;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.Storage;
using NoCTF.Application.Teams.WriteUps;
using NoCTF.Domain.Competitions;
using NSubstitute;

namespace NoCTF.Tests.Unit.Application;

public sealed class TeamWriteUpTests
{
    [Test]
    public async Task ReplaceMineAsync_rejects_non_pdf_content_before_creating_a_file()
    {
        var store = Substitute.For<ITeamWriteUpStore>();
        var registry = Substitute.For<IManagedFileUploadRegistry>();
        var objects = Substitute.For<IStore>();
        var writeUps = new ManageTeamWriteUps(
            store,
            new ManagedFileUploads(registry, objects),
            objects,
            Substitute.For<ILeaderboardCache>());
        await using var content = new MemoryStream("not a pdf"u8.ToArray());

        var result = await writeUps.ReplaceMineAsync(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "writeup.pdf",
            TeamWriteUpRules.ContentType,
            content.Length,
            content,
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        await Assert.That(result.State).IsEqualTo(TeamWriteUpSubmissionState.InvalidPdf);
        await store.DidNotReceive().FindEligibleTeamIdAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
        await registry.DidNotReceive().RegisterAsync(
            Arg.Any<ManagedFileUpload>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ReviewAsync_aggregates_every_round_column_into_challenge_scores()
    {
        var competitionId = Guid.CreateVersion7();
        var teamId = Guid.CreateVersion7();
        var firstChallengeId = Guid.CreateVersion7();
        var secondChallengeId = Guid.CreateVersion7();
        var writeUp = new TeamWriteUpReference(
            teamId,
            "Team",
            Guid.CreateVersion7(),
            "writeups/team.pdf",
            "team.pdf",
            TeamWriteUpRules.ContentType,
            100,
            new string('A', 64),
            Guid.CreateVersion7(),
            "Player",
            DateTimeOffset.UtcNow);
        var store = Substitute.For<ITeamWriteUpStore>();
        store.ListAsync(competitionId, Arg.Any<CancellationToken>())
            .Returns([writeUp]);
        var leaderboard = Substitute.For<ILeaderboardCache>();
        leaderboard.GetScoreboardAsync(competitionId, Arg.Any<CancellationToken>())
            .Returns(Projection(
                competitionId,
                teamId,
                firstChallengeId,
                secondChallengeId));
        var objects = Substitute.For<IStore>();
        var writeUps = new ManageTeamWriteUps(
            store,
            new ManagedFileUploads(
                Substitute.For<IManagedFileUploadRegistry>(),
                objects),
            objects,
            leaderboard);

        var result = await writeUps.ReviewAsync(competitionId, CancellationToken.None);

        await Assert.That(result.ScoreboardAvailable).IsTrue();
        await Assert.That(result.Items).Count().IsEqualTo(1);
        await Assert.That(result.Items[0].TotalScore).IsEqualTo(22);
        await Assert.That(result.Items[0].ChallengeScores).Count().IsEqualTo(2);
        await Assert.That(result.Items[0].ChallengeScores[0].NetPoints).IsEqualTo(15);
        await Assert.That(result.Items[0].ChallengeScores[1].NetPoints).IsEqualTo(7);
    }

    private static ScoreboardProjection Projection(
        Guid competitionId,
        Guid teamId,
        Guid firstChallengeId,
        Guid secondChallengeId)
    {
        var catalog = new ScoreboardChallengeCatalog(
            competitionId,
            1,
            [
                new(firstChallengeId, "First", "Web", "Web", 1, true),
                new(secondChallengeId, "Second", "Pwn", "Pwn", 2, true)
            ]);
        var schema = new ScoreboardSchema(
            competitionId,
            GameMode.Ctf,
            1,
            1,
            [],
            [
                new(0, firstChallengeId, Guid.CreateVersion7()),
                new(1, firstChallengeId, Guid.CreateVersion7()),
                new(2, secondChallengeId, Guid.CreateVersion7())
            ]);
        var team = new ScoreboardTeam(
            teamId,
            "Team",
            "default",
            1,
            ScoreboardRankingState.Eligible,
            22,
            0,
            [],
            [Slot(0, 10), Slot(1, 5), Slot(2, 7)]);
        var snapshot = new ScoreboardSnapshot(
            competitionId,
            1,
            1,
            DateTimeOffset.UtcNow,
            null,
            [],
            [team]);
        return new(catalog, schema, snapshot);
    }

    private static ScoreboardSlot Slot(int columnIndex, long score) =>
        new(
            columnIndex,
            ScoreboardScoreState.Settled,
            score,
            0,
            score,
            1,
            [],
            []);
}
