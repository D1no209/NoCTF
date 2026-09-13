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
    public async Task Submission_is_open_during_the_competition_and_closes_at_the_deadline()
    {
        var endAt = DateTimeOffset.Parse("2026-09-13T02:00:00Z");

        await Assert.That(CompetitionWriteUpPolicy.CanSubmit(
            endAt,
            0,
            endAt.AddHours(-1))).IsTrue();
        await Assert.That(CompetitionWriteUpPolicy.CanSubmit(
            endAt,
            0,
            endAt)).IsTrue();
        await Assert.That(CompetitionWriteUpPolicy.CanSubmit(
            endAt,
            0,
            endAt.AddTicks(1))).IsFalse();
    }

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
        await store.DidNotReceive().FindSubmissionContextAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
        await registry.DidNotReceive().RegisterAsync(
            Arg.Any<ManagedFileUpload>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ReplaceMineAsync_rejects_an_expired_submission_before_upload()
    {
        var now = DateTimeOffset.Parse("2026-09-13T02:00:00Z");
        var competitionId = Guid.CreateVersion7(now);
        var actorId = Guid.CreateVersion7(now.AddTicks(1));
        var store = Substitute.For<ITeamWriteUpStore>();
        store.FindSubmissionContextAsync(
                competitionId,
                actorId,
                Arg.Any<CancellationToken>())
            .Returns(new TeamWriteUpSubmissionContext(
                Guid.CreateVersion7(now.AddTicks(2)),
                now.AddHours(-1),
                0));
        var registry = Substitute.For<IManagedFileUploadRegistry>();
        var objects = Substitute.For<IStore>();
        var writeUps = new ManageTeamWriteUps(
            store,
            new ManagedFileUploads(registry, objects),
            objects,
            Substitute.For<ILeaderboardCache>());
        await using var content = new MemoryStream("%PDF-1.7\n%%EOF"u8.ToArray());

        var result = await writeUps.ReplaceMineAsync(
            competitionId,
            actorId,
            "writeup.pdf",
            TeamWriteUpRules.ContentType,
            content.Length,
            content,
            now,
            CancellationToken.None);

        await Assert.That(result.State)
            .IsEqualTo(TeamWriteUpSubmissionState.SubmissionDeadlinePassed);
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
        store.ReadManualAdjustmentsAsync(
                competitionId,
                Arg.Any<CancellationToken>())
            .Returns([new TeamWriteUpManualAdjustment(teamId, firstChallengeId, 10)]);
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
        await Assert.That(result.Items[0].OriginalTotalScore).IsEqualTo(12);
        await Assert.That(result.Items[0].OriginalRank).IsEqualTo(2);
        await Assert.That(result.Items[0].AdjustedTotalScore).IsEqualTo(22);
        await Assert.That(result.Items[0].AdjustedRank).IsEqualTo(1);
        await Assert.That(result.Items[0].ChallengeScores).Count().IsEqualTo(2);
        await Assert.That(result.Items[0].ChallengeScores[0].NetPoints).IsEqualTo(15);
        await Assert.That(result.Items[0].ChallengeScores[1].NetPoints).IsEqualTo(7);
    }

    [Test]
    public async Task ReviewAsync_with_no_submissions_skips_the_scoreboard_dependency()
    {
        var competitionId = Guid.CreateVersion7();
        var store = Substitute.For<ITeamWriteUpStore>();
        store.ListAsync(competitionId, Arg.Any<CancellationToken>())
            .Returns([]);
        var leaderboard = Substitute.For<ILeaderboardCache>();
        var objects = Substitute.For<IStore>();
        var writeUps = new ManageTeamWriteUps(
            store,
            new ManagedFileUploads(
                Substitute.For<IManagedFileUploadRegistry>(),
                objects),
            objects,
            leaderboard);

        var result = await writeUps.ReviewAsync(competitionId, CancellationToken.None);

        await Assert.That(result.ScoreboardAvailable).IsFalse();
        await Assert.That(result.Items).IsEmpty();
        await leaderboard.DidNotReceive().GetScoreboardAsync(
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
        await store.DidNotReceive().ReadManualAdjustmentsAsync(
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
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
            [Slot(0, 5), Slot(1, 0), Slot(2, 7)]);
        var secondTeam = new ScoreboardTeam(
            Guid.CreateVersion7(),
            "Second team",
            "default",
            2,
            ScoreboardRankingState.Eligible,
            20,
            0,
            [],
            []);
        var snapshot = new ScoreboardSnapshot(
            competitionId,
            1,
            1,
            DateTimeOffset.UtcNow,
            null,
            [],
            [team, secondTeam]);
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
