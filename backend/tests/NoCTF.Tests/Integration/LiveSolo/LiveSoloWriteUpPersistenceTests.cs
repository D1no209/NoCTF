using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NoCTF.Application.Challenges.WriteUps;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Challenges.WriteUps;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.LiveSolo;
using NoCTF.Infrastructure.Challenges.Management;
using NoCTF.Infrastructure.Challenges.WriteUps;
using NoCTF.Infrastructure.LiveSolo.Resources;
using NoCTF.Infrastructure.Persistence;
using FluentStorage.Storage;
using NoCTF.Application.Storage;
using NoCTF.Application.LiveSolo.Resources;
using NoCTF.Domain.Storage;
using NoCTF.Infrastructure.Teams.Moderation;
using NoCTF.Tests.Integration.Persistence;

namespace NoCTF.Tests.Integration.LiveSolo;

[Category("Integration")]
public sealed class LiveSoloWriteUpPersistenceTests
{
    [Test,Timeout(300_000)]
    public async Task Scoped_postgame_submission_review_and_read_never_use_paid_unlock_or_ordinary_routes(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async()=>
        {
            await using var f=await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);await f.PrepareAsync(ct);await f.StartAsync(ct);
            var question=f.Round.Questions.Single(x=>x.Position==0);var store=Store(f.Db);var competition=await f.Db.Competitions.SingleAsync(ct);
            competition.SingleWriteUpsEnabled=true;await f.Db.SaveChangesAsync(ct);
            var command=new SaveWriteUpDraft(f.Competition.Id,question.CompetitionChallengeId,f.Left.Id,false,WriteUpFormat.Markdown,"# Solution",null,null,f.Now,question.Id);
            await Assert.That((await store.SaveDraftAsync(command,ct)).Failure).IsEqualTo(ChallengeWriteUpFailure.Forbidden);
            competition.Status=CompetitionStatus.Finished;(await f.Db.LiveSoloMatches.SingleAsync(ct)).State=LiveSoloMatchState.Completed;await f.Db.SaveChangesAsync(ct);
            await Assert.That((await store.SaveDraftAsync(command with {ExecutionScopeId=null},ct)).Failure).IsEqualTo(ChallengeWriteUpFailure.Forbidden);
            var draft=await store.SaveDraftAsync(command,ct);await Assert.That(draft.Failure).IsNull();
            var submitted=await store.SubmitAsync(new(f.Competition.Id,question.CompetitionChallengeId,f.Left.Id,false,draft.WriteUp!.ConcurrencyStamp,f.Now,question.Id),ct);
            await Assert.That(submitted.Failure).IsNull();
            var version=submitted.WriteUp!.Submitted!.Id;
            var publication=await store.ReviewAsync(new(f.Competition.Id,question.CompetitionChallengeId,submitted.WriteUp.Id,version,f.Owner.Id,
                submitted.WriteUp.ConcurrencyStamp,WriteUpReviewAction.Publish,null,f.Now,question.Id),ct);
            await Assert.That(publication.Failure).IsNull();
            await Assert.That((await f.Db.LiveSoloQuestionExposures.SingleAsync(ct)).CanonicalChallengeId).IsEqualTo(f.Templates[0].Id);
            var listed=await store.ListAsync(f.Competition.Id,question.CompetitionChallengeId,f.Right.Id,false,f.Now,ct,question.Id);
            await Assert.That(listed!.Access.IsFree).IsTrue();await Assert.That(listed.Access.DeductionPercent).IsEqualTo(0);await Assert.That(listed.Access.CanUnlock).IsFalse();
            var body=await store.ReadContentAsync(f.Competition.Id,question.CompetitionChallengeId,version,f.Right.Id,false,f.Now,ct,question.Id);
            await Assert.That(body.Markdown).IsEqualTo("# Solution");
            await Assert.That((await store.ReadContentAsync(f.Competition.Id,question.CompetitionChallengeId,version,f.Owner.Id,true,f.Now,ct)).Failure).IsEqualTo(ChallengeWriteUpFailure.Forbidden);
            await Assert.That(await f.Db.WriteUpUnlockReceipts.CountAsync(ct)).IsEqualTo(0);await Assert.That(await f.Db.GameplayFacts.CountAsync(ct)).IsEqualTo(0);
            var withdrawal=await store.ReviewAsync(new(f.Competition.Id,question.CompetitionChallengeId,publication.WriteUp!.Id,version,f.Owner.Id,
                publication.WriteUp.ConcurrencyStamp,WriteUpReviewAction.Withdraw,null,f.Now,question.Id),ct);
            await Assert.That(withdrawal.Failure).IsNull();
            await Assert.That((await store.ReadContentAsync(f.Competition.Id,question.CompetitionChallengeId,version,f.Right.Id,false,f.Now,ct,question.Id)).Failure).IsEqualTo(ChallengeWriteUpFailure.NotPublished);
        });
    }
    [Test,Timeout(300_000)]
    public async Task Historical_scope_binding_rejects_cross_match_unopened_and_banned_accounts(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async()=>
        {
            await using var f=await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);await f.PrepareAsync(ct);await f.StartAsync(ct);
            (await f.Db.Competitions.SingleAsync(ct)).Status=CompetitionStatus.Finished;await f.Db.SaveChangesAsync(ct);
            var question=f.Round.Questions.Single(x=>x.Position==0);var access=new LiveSoloPostgameQuestionAccess(f.Db,new CompetitionModerationAuthorizer(f.Db));
            var request=new NoCTF.Application.LiveSolo.Resources.LiveSoloResourceRequest(f.Competition.Id,f.Match.Id,f.Round.Id,question.Id,f.Left.Id,f.Now);
            await Assert.That(await access.ResolveAsync(request,ct)).IsEqualTo(question.CompetitionChallengeId);
            await Assert.That(await access.ResolveAsync(request with {MatchId=Guid.NewGuid()},ct)).IsNull();
            await Assert.That(await access.ResolveAsync(request with {RoundId=Guid.NewGuid()},ct)).IsNull();
            await Assert.That(await access.ResolveAsync(request with {QuestionId=f.Round.Questions.Single(x=>x.Position==1).Id},ct)).IsNull();
            (await f.Db.Teams.SingleAsync(x=>x.Id==f.LeftTeam.Id,ct)).IsBanned=true;await f.Db.SaveChangesAsync(ct);
            await Assert.That(await access.ResolveAsync(request,ct)).IsNull();
        });
    }
    [Test,Timeout(300_000)]
    public async Task PDF_stream_rechecks_publication_after_opening_and_same_competition_approved_non_roster_teams_read_public_solutions(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async()=>
        {
            await using var f=await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);await f.PrepareAsync(ct);await f.StartAsync(ct);
            var comp=await f.Db.Competitions.SingleAsync(ct);comp.Status=CompetitionStatus.Finished;comp.SingleWriteUpsEnabled=true;await f.Db.SaveChangesAsync(ct);
            var q=f.Round.Questions.Single(x=>x.Position==0);var file=new StoredFile {Id=Guid.NewGuid(),ObjectKey="postgame.pdf",FileName="postgame.pdf",ContentType="application/pdf",ByteLength=4,Sha256=new byte[32],CreatedAt=f.Now};
            f.Db.Files.Add(file);await f.Db.SaveChangesAsync(ct);var store=Store(f.Db);
            var draft=await store.SaveDraftAsync(new(f.Competition.Id,q.CompetitionChallengeId,f.Left.Id,false,WriteUpFormat.Pdf,null,file.Id,null,f.Now,q.Id),ct);
            var submitted=await store.SubmitAsync(new(f.Competition.Id,q.CompetitionChallengeId,f.Left.Id,false,draft.WriteUp!.ConcurrencyStamp,f.Now,q.Id),ct);
            var version=submitted.WriteUp!.Submitted!.Id;
            await store.ReviewAsync(new(f.Competition.Id,q.CompetitionChallengeId,submitted.WriteUp.Id,version,f.Owner.Id,submitted.WriteUp.ConcurrencyStamp,WriteUpReviewAction.Publish,null,f.Now,q.Id),ct);
            await f.Db.Set<LiveSoloRosterMember>().Where(x=>x.MatchId==f.Match.Id&&x.UserId==f.Right.Id).ExecuteDeleteAsync(ct);
            await Assert.That((await store.ReadContentAsync(f.Competition.Id,q.CompetitionChallengeId,version,f.Right.Id,false,f.Now,ct,q.Id)).Failure).IsNull();
            var objects=Substitute.For<IStore>();var stream=new MemoryStream([1,2,3,4]);
            async Task<Stream?> OpenAndWithdraw(){await using var db=new NoCtfDbContext(f.Options);(await db.ChallengeWriteUps.SingleAsync(ct)).PublishedVersionId=null;await db.SaveChangesAsync(ct);return stream;}
            objects.OpenRead("postgame.pdf",ct).Returns(_=>OpenAndWithdraw());
            var service=new ManageLiveSoloWriteUps(new LiveSoloPostgameQuestionAccess(f.Db,new CompetitionModerationAuthorizer(f.Db)),new ManageChallengeWriteUps(store,new ManagedFileUploads(Substitute.For<IManagedFileUploadRegistry>(),objects),objects));
            var opened=await service.OpenPdfAsync(new(f.Competition.Id,f.Match.Id,f.Round.Id,q.Id,f.Right.Id,f.Now),version,false,ct);
            await Assert.That(opened).IsNull();await Assert.That(stream.CanRead).IsFalse();await Assert.That(await f.Db.GameplayFacts.CountAsync(ct)).IsEqualTo(0);
        });
    }
    private static ChallengeWriteUpStore Store(NoCtfDbContext db)=>new(db,new CompetitionModerationAuthorizer(db),Substitute.For<NoCTF.Application.Challenges.Management.ICompetitionChallengeReadAccess>(),
        NullCompetitionEventRecorder.Instance,Substitute.For<IPostCommitMessagePublisher>());
    [Test,Timeout(300_000)]
    public async Task Postgame_directory_executes_relational_ordering_and_preserves_replay_scopes_and_access_rules(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async()=>{
            await using var f=await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);await f.PrepareAsync(ct);await f.StartAsync(ct);
            var access=new LiveSoloPostgameQuestionAccess(f.Db,new CompetitionModerationAuthorizer(f.Db));
            await Assert.That(await access.ListAsync(f.Competition.Id,f.Match.Id,f.Left.Id,ct)).IsNull();
            (await f.Db.Competitions.SingleAsync(ct)).Status=CompetitionStatus.Finished;await f.Db.SaveChangesAsync(ct);
            var first=f.Round.Questions.Single(x=>x.Position==0);var second=f.Round.Questions.Single(x=>x.Position==1);
            var initial=await access.ListAsync(f.Competition.Id,f.Match.Id,f.Left.Id,ct);
            await Assert.That(initial!.Select(x=>x.Id)).IsEquivalentTo(new[]{first.Id});
            second.OpenedAt=f.Now;var replay=new LiveSoloRound {Id=Guid.NewGuid(),MatchId=f.Match.Id,Number=f.Round.Number,Replay=1,
                QuestionGroupId=f.Round.QuestionGroupId,CreatedAt=f.Now,State=LiveSoloRoundState.Won};
            var replayQuestion=new LiveSoloRoundQuestion {Id=Guid.NewGuid(),RoundId=replay.Id,CompetitionChallengeId=first.CompetitionChallengeId,Position=0,OpenedAt=f.Now};
            replay.Questions.Add(replayQuestion);f.Db.LiveSoloRounds.Add(replay);await f.Db.SaveChangesAsync(ct);
            var listed=await access.ListAsync(f.Competition.Id,f.Match.Id,f.Left.Id,ct)??throw new InvalidOperationException("Expected scoped directory.");
            await Assert.That(listed.Select(x=>x.Id).SequenceEqual(new[]{first.Id,second.Id,replayQuestion.Id})).IsTrue();
            await Assert.That(listed.Last().Replay).IsEqualTo(1);await Assert.That(listed.Last().RoundId).IsEqualTo(replay.Id);
            await Assert.That(await access.ListAsync(Guid.NewGuid(),f.Match.Id,f.Left.Id,ct)).IsNull();
            await Assert.That(await access.ListAsync(f.Competition.Id,Guid.NewGuid(),f.Left.Id,ct)).IsNull();
            (await f.Db.Teams.SingleAsync(x=>x.Id==f.LeftTeam.Id,ct)).IsBanned=true;await f.Db.SaveChangesAsync(ct);
            await Assert.That(await access.ListAsync(f.Competition.Id,f.Match.Id,f.Left.Id,ct)).IsNull();
        });
    }
}
