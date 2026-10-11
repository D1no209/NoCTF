using FluentStorage.Storage;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NoCTF.Application.LiveSolo.Adjudication;
using NoCTF.Application.LiveSolo.Matches;
using NoCTF.Application.LiveSolo.Resources;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.LiveSolo;
using NoCTF.Domain.Storage;
using NoCTF.Infrastructure.LiveSolo.Resources;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Teams.Moderation;
using NoCTF.Tests.Integration.Persistence;

namespace NoCTF.Tests.Integration.LiveSolo;

[Category("Integration")]
public sealed class LiveSoloReviewAndResultsTests
{
    [Test, Timeout(300_000)]
    public async Task Disclosed_material_is_frozen_and_only_the_valid_locked_roster_can_review_during_the_competition(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct, false);
            f.Templates[0].Direction = new string('d', 96); await f.Db.SaveChangesAsync(ct);
            await f.PrepareAsync(ct); await f.StartAsync(ct);
            var q = f.Round.Questions.Single(x => x.Position == 0); var future = f.Round.Questions.Single(x => x.Position == 1);
            var reader = new LiveSoloReviewStore(f.Db, new CompetitionModerationAuthorizer(f.Db));
            var request = new LiveSoloReviewRequest(f.Competition.Id, f.Match.Id, f.Left.Id, false, f.Now);
            await Assert.That((await reader.ListAsync(request, ct))!.Count).IsEqualTo(1);
            await Assert.That(await reader.ReadAsync(request, future.Id, ct)).IsNull();
            var staff = request with { ActorId = f.Owner.Id, Staff = true };
            await Assert.That((await reader.ListAsync(staff, ct))!.Count).IsEqualTo(2);
            await Assert.That((await reader.ReadAsync(staff, future.Id, ct))!.Question.Description).IsEqualTo("secret body 1");
            await Assert.That(await reader.ReadAsync(request with { Staff = true }, future.Id, ct)).IsNull();
            await Assert.That(await reader.ReadAsync(request with { MatchId = Guid.NewGuid() }, q.Id, ct)).IsNull();
            f.Templates[0].Description = "future secret after edit"; f.Templates[0].Title = "Changed";
            await f.Db.SaveChangesAsync(ct);
            var replay = await reader.ReadAsync(request, q.Id, ct);
            await Assert.That(replay!.Question.Description).IsEqualTo("secret body 0");
            await Assert.That(replay.Question.Title).IsEqualTo("Question 0");
            await Assert.That(replay.Question.Direction).IsEqualTo(new string('d', 96));
            await Assert.That(replay.Question.RuntimeInstanceId).IsNull();
            f.LeftTeam.IsBanned = true; await f.Db.SaveChangesAsync(ct);
            await Assert.That(await reader.ReadAsync(request, q.Id, ct)).IsNull();
            f.LeftTeam.IsBanned = false; f.Left.AccountStatus = UserAccountStatus.Banned; await f.Db.SaveChangesAsync(ct);
            await Assert.That(await reader.ReadAsync(request, q.Id, ct)).IsNull();
        });
    }

    [Test, Timeout(300_000)]
    public async Task Review_keeps_the_original_file_and_never_records_download_evidence_and_rechecks_after_opening(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct, false);
            var file = new StoredFile { Id = Guid.NewGuid(), ObjectKey = "synthetic-original", FileName = "original.txt",
                ContentType = "text/plain", ByteLength = 4, Sha256 = new byte[32], CreatedAt = f.Now };
            var attachment = new ChallengeAttachment { Id = Guid.NewGuid(), ChallengeId = f.Templates[0].Id, FileId = file.Id, CreatedAt = f.Now };
            f.Db.Files.Add(file); f.Db.Add(attachment); await f.Db.SaveChangesAsync(ct);
            await f.PrepareAsync(ct); await f.StartAsync(ct);
            var q = f.Round.Questions.Single(x => x.Position == 0);
            attachment.DeletedAt = f.Now; await f.Db.SaveChangesAsync(ct);
            var reader = new LiveSoloReviewStore(f.Db, new CompetitionModerationAuthorizer(f.Db));
            var request = new LiveSoloReviewRequest(f.Competition.Id, f.Match.Id, f.Left.Id, false, f.Now);
            await Assert.That((await reader.FileAsync(request, q.Id, attachment.Id, ct))!.FileId).IsEqualTo(file.Id);
            var objects = Substitute.For<IStore>(); objects.ObjectExists(file.ObjectKey, ct).Returns(true);
            objects.OpenRead(file.ObjectKey, ct).Returns(_ => new MemoryStream([1, 2, 3, 4]));
            var service = new ReadLiveSoloReview(reader, objects);
            var opened = await service.OpenAsync(request, q.Id, attachment.Id, ct);
            await Assert.That(opened).IsNotNull(); await opened!.Value.Content.DisposeAsync();
            await Assert.That(await f.Db.GameplayFacts.CountAsync(ct)).IsEqualTo(0);
            async Task<Stream> BanAndOpen() { f.LeftTeam.IsBanned = true; await f.Db.SaveChangesAsync(ct); return new MemoryStream([1]); }
            objects.OpenRead(file.ObjectKey, ct).Returns(_ => BanAndOpen());
            await Assert.That(await service.OpenAsync(request, q.Id, attachment.Id, ct)).IsNull();
            await Assert.That(await f.Db.LiveSoloDownloadEvidences.CountAsync(ct)).IsEqualTo(0);
        });
    }

    [Test, Timeout(300_000)]
    public async Task Winner_confirmation_is_authoritative_monotonic_and_repeat_processing_does_not_duplicate_results(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct, false); await f.PrepareAsync(ct); await f.StartAsync(ct);
            var store = f.Store(f.Db); var q = f.Round.Questions.Single(x => x.Position == 0);
            await store.AdmitAsync(new(f.Competition.Id, f.Match.Id, f.Round.Id, q.Id, f.Left.Id, "flag{first}", f.Now), ct);
            await store.ResolveAsync(f.Round.Id, f.Now, ct); await store.ResolveAsync(f.Round.Id, f.Now, ct);
            var workspace = await ((ILiveSoloWorkspaceReader)store).ReadAsync(f.Competition.Id, f.Match.Id, f.Left.Id, false, f.Now, ct);
            await Assert.That(workspace!.Match.ResultRevision).IsEqualTo(1);
            await Assert.That(workspace.Round!.ResultRevision).IsEqualTo(1);
            await Assert.That(workspace.Match.EndReason).IsEqualTo(LiveSoloEndReason.RequiredWinsReached);
            await Assert.That(workspace.Round.EndReason).IsEqualTo(LiveSoloEndReason.CorrectAnswer);
            await Assert.That(workspace.Match.ResultLeftWins).IsEqualTo(1);
            await Assert.That(workspace.Round.ResultLeftWins).IsEqualTo(1);
            await Assert.That(workspace.History.Count).IsEqualTo(1);
            await Assert.That(workspace.AllowedOperations.Contains(LiveSoloWorkspaceOperation.SubmitFlag)).IsFalse();
        });
    }

    [Test, Timeout(300_000)]
    public async Task Forfeit_preserves_the_real_tally_and_timeout_has_no_fabricated_winner(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct, false); await f.PrepareAsync(ct);
            var store = f.Store(f.Db); var match = (await store.FindAsync(f.Competition.Id, f.Match.Id, f.Owner.Id, true, f.Now, ct))!;
            var result = await store.ApplyAsync(new(f.Competition.Id, match.Id, f.Owner.Id, match.ConcurrencyStamp, f.Round.Id,
                f.Round.ConcurrencyStamp, f.Round.TimelineRevision, LiveSoloJudgeAction.ForfeitMatch, f.LeftTeam.Id, "Synthetic forfeit"), ct);
            await Assert.That(result.Failure).IsNull();
            await Assert.That(result.Match!.EndReason).IsEqualTo(LiveSoloEndReason.Forfeit);
            await Assert.That(result.Match.RightWins).IsEqualTo(0);
            await Assert.That(result.Match.WinnerTeamId).IsEqualTo(f.RightTeam.Id);
            await using var timeout = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct, false); await timeout.PrepareAsync(ct); await timeout.StartAsync(ct);
            timeout.Now = timeout.Now.AddSeconds(31);
            await timeout.Store(timeout.Db).ResolveAsync(timeout.Round.Id, timeout.Now, ct);
            await Assert.That(timeout.Round.WinnerTeamId).IsNull();
            await Assert.That(timeout.Round.EndReason).IsEqualTo(LiveSoloEndReason.TimeLimit);
            await Assert.That(timeout.Round.ResultLeftWins).IsEqualTo(0);
        });
    }

    [Test, Timeout(300_000)]
    public async Task Workspace_explains_preparation_and_future_content_never_enters_the_player_projection(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct, false); await f.PrepareAsync(ct);
            f.Competition.Status = CompetitionStatus.Published; await f.Db.SaveChangesAsync(ct);
            var store = (ILiveSoloWorkspaceReader)f.Store(f.Db);
            var staff = await store.ReadAsync(f.Competition.Id, f.Match.Id, f.Owner.Id, true, f.Now, ct);
            await Assert.That(staff!.MissingConditions.Contains(LiveSoloStartCondition.CompetitionNotRunning)).IsTrue();
            await Assert.That(staff.AllowedOperations.Contains(LiveSoloWorkspaceOperation.StartCountdown)).IsFalse();
            var player = await store.ReadAsync(f.Competition.Id, f.Match.Id, f.Left.Id, false, f.Now, ct);
            await Assert.That(player!.Questions.Count).IsEqualTo(0); await Assert.That(player.History.Count).IsEqualTo(0);
            await Assert.That(player.AllowedOperations.Contains(LiveSoloWorkspaceOperation.ManageQuestionPool)).IsFalse();
        });
    }

    [Test, Timeout(300_000)]
    public async Task Legacy_unknown_results_are_initialized_once_without_inventing_the_reason_score_or_disclosed_content(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct, false); await f.PrepareAsync(ct);
            var match = await f.Db.LiveSoloMatches.SingleAsync(ct); match.State = LiveSoloMatchState.Completed; match.CompletedAt = f.Now;
            f.Round.State = LiveSoloRoundState.Canceled; f.Round.EndedAt = f.Now; await f.Db.SaveChangesAsync(ct);
            var store = (ILiveSoloWorkspaceReader)f.Store(f.Db);
            var first = (await store.ReadAsync(f.Competition.Id, f.Match.Id, f.Owner.Id, true, f.Now, ct))!;
            var again = (await store.ReadAsync(f.Competition.Id, f.Match.Id, f.Owner.Id, true, f.Now, ct))!;
            await Assert.That(again.Match.ResultRevision).IsEqualTo(first.Match.ResultRevision);
            await Assert.That(again.Match.EndReason).IsEqualTo(LiveSoloEndReason.Unrecorded);
            await Assert.That(again.Match.WinnerTeamId).IsNull();
            await Assert.That(again.Match.ResultLeftWins).IsNull();
            await Assert.That(again.Round!.EndReason).IsEqualTo(LiveSoloEndReason.Unrecorded);
            await Assert.That(again.Round.ResultLeftWins).IsNull();
        });
    }

    [Test, Timeout(300_000)]
    public async Task Failed_future_preparation_stops_automatic_retries_and_staff_can_retry_the_same_question_during_play(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct, false); await f.PrepareAsync(ct); await f.StartAsync(ct);
            var question = f.Round.Questions.Single(x => x.Position == 1);
            question.Readiness = LiveSoloQuestionReadiness.Failed; question.PreparationFailure = LiveSoloResourceFailure.DependencyUnavailable;
            await f.Db.SaveChangesAsync(ct); f.Now = f.Now.AddSeconds(12);
            await Assert.That(NoCTF.Application.LiveSolo.Rounds.LiveSoloRoundSchedulePolicy.PreparationCandidates(f.Round, f.Now)).IsEmpty();
            var store = f.Store(f.Db);
            var player = await ((ILiveSoloWorkspaceReader)store).ReadAsync(f.Competition.Id, f.Match.Id, f.Left.Id, false, f.Now, ct);
            await Assert.That(player!.Resources.Count).IsEqualTo(0);
            var staff = await ((ILiveSoloWorkspaceReader)store).ReadAsync(f.Competition.Id, f.Match.Id, f.Owner.Id, true, f.Now, ct);
            await Assert.That(staff!.Resources.Single(x => x.QuestionId == question.Id).Failure).IsEqualTo(LiveSoloResourceFailure.DependencyUnavailable);
            await Assert.That(staff.AllowedOperations.Contains(LiveSoloWorkspaceOperation.RetryPreparation)).IsTrue();
            var retried = await store.RetryPreparationAsync(new(f.Competition.Id, f.Match.Id, f.Round.Id, f.Owner.Id, staff.Round!.ConcurrencyStamp, f.Now, question.Id), ct);
            await Assert.That(retried.Failure).IsNull(); await Assert.That(retried.Round!.Id).IsEqualTo(f.Round.Id);
            await store.TickAsync(f.Round.Id, retried.Round.TimelineRevision, f.Now, ct);
            await Assert.That(question.Readiness).IsEqualTo(LiveSoloQuestionReadiness.Ready);
            await Assert.That(question.OpenedAt).IsNotNull();
        });
    }
}
