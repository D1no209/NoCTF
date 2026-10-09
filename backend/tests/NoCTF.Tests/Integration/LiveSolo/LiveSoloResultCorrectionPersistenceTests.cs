using Microsoft.EntityFrameworkCore;
using NoCTF.Application.LiveSolo.Adjudication;
using NoCTF.Application.LiveSolo.Brackets;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Domain.LiveSolo;
using NoCTF.Domain.Gameplay;
using NoCTF.Tests.Integration.Persistence;

namespace NoCTF.Tests.Integration.LiveSolo;

[Category("Integration")]
public sealed class LiveSoloResultCorrectionPersistenceTests
{
    [Test,Timeout(300_000)]
    public async Task Correction_freezes_downstream_requires_replay_consent_and_preserves_old_teams_scopes_and_correct_facts(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f=await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);
            var source=await FinishedSource(f,ct);var store=f.Store(f.Db);
            var downstream=await StartedFinal(f,source,ct);
            var question=downstream.Rounds.Single().Questions.Single();
            var pending=await store.AdmitAsync(new(f.Competition.Id,downstream.Id,question.RoundId,question.Id,f.Left.Id,"flag{wrong}",f.Now),ct);
            await Assert.That(pending.Failure).IsNull();
            var proposal=new LiveSoloCorrectionProposal(f.Competition.Id,source.Id,f.Owner.Id,f.RightTeam.Id,0,1);
            var preview=await store.PreviewAsync(proposal,ct);await Assert.That(preview).IsNotNull();
            await Assert.That(preview!.Downstream.Count(x=>x.RequiresReplay)).IsEqualTo(1);
            await Assert.That(await f.Db.Set<LiveSoloResultCorrection>().CountAsync(ct)).IsEqualTo(0);
            var begun=await new ManageLiveSoloResultCorrections(store).BeginAsync(new(proposal,preview.PreviewId,"reviewed incorrect outcome"),ct);
            await Assert.That(begun.Failure).IsNull();var correction=begun.Correction!;
            await Assert.That(downstream.State).IsEqualTo(LiveSoloMatchState.Paused);
            await Assert.That((await store.AdmitAsync(new(f.Competition.Id,downstream.Id,question.RoundId,question.Id,f.Left.Id,"flag{first}",f.Now),ct)).Failure).IsEqualTo(LiveSoloFailure.RoundPaused);
            await store.ResolveAsync(question.RoundId,f.Now,ct);await Assert.That(downstream.WinnerTeamId).IsNull();
            var missing=await new ManageLiveSoloResultCorrections(store).ResolveAsync(new(f.Competition.Id,source.Id,f.Owner.Id,correction.Id,
                correction.ConcurrencyStamp,true,"void and replay",[]),ct);
            await Assert.That(missing.Failure).IsEqualTo(LiveSoloFailure.Conflict);
            var consents=correction.Downstream.Where(x=>x.RequiresReplay).Select(x=>new LiveSoloReplayConsent(x.MatchId,x.ConcurrencyStamp)).ToArray();
            var applied=await new ManageLiveSoloResultCorrections(store).ResolveAsync(new(f.Competition.Id,source.Id,f.Owner.Id,correction.Id,
                correction.ConcurrencyStamp,true,"void and replay after review",consents),ct);
            await Assert.That(applied.Failure).IsNull();await Assert.That(applied.Correction!.State).IsEqualTo(LiveSoloCorrectionState.Applied);
            await Assert.That(source.WinnerTeamId).IsEqualTo(f.RightTeam.Id);
            await Assert.That(downstream.State).IsEqualTo(LiveSoloMatchState.Canceled);
            await Assert.That(downstream.Slots.Single(x=>x.Side==LiveSoloSide.Left).TeamId).IsEqualTo(f.LeftTeam.Id);
            await Assert.That(downstream.Roster.Count).IsEqualTo(2);
            var replacement=await f.Db.LiveSoloMatches.Include(x=>x.Slots).SingleAsync(x=>x.Id==downstream.ReplacementMatchId,ct);
            await Assert.That(replacement.Id).IsNotEqualTo(downstream.Id);await Assert.That(replacement.BracketGeneration).IsEqualTo(1);
            await Assert.That(replacement.Slots.Single(x=>x.Side==LiveSoloSide.Left).TeamId).IsEqualTo(f.RightTeam.Id);
            await Assert.That(replacement.CurrentRoundId).IsNull();
            await Assert.That(await f.Db.LiveSoloActiveTeamSlots.CountAsync(x=>x.MatchId==replacement.Id,ct)).IsEqualTo(2);
            await Assert.That((await f.Db.GameplayFacts.SingleAsync(x=>x.Id==pending.GameplayFactId,ct)).FailureCode).IsEqualTo(GameplayFactFailureCode.RoundOutOfRange);
            await Assert.That(await f.Db.GameplayFacts.CountAsync(x=>x.Result==GameplayFactResult.Correct,ct)).IsEqualTo(1);
            var bracket=await store.ReadAsync(f.Competition.Id,f.Owner.Id,ct);
            await Assert.That(bracket!.Matches.Count).IsEqualTo(3);
            await Assert.That(bracket.Matches.Any(x=>x.Match.Id==downstream.Id)).IsFalse();
            var audit=await f.Db.Set<LiveSoloResultCorrection>().SingleAsync(ct);audit.Reason="tamper";
            await Assert.That(async()=>await f.Db.SaveChangesAsync(ct)).Throws<InvalidOperationException>();
        });
    }
    [Test,Timeout(300_000)]
    public async Task Cancel_restores_the_prior_outcome_and_pause_clock_and_judges_cannot_begin_corrections(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f=await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);var source=await FinishedSource(f,ct);
            var downstream=await StartedFinal(f,source,ct);var store=f.Store(f.Db);
            var proposal=new LiveSoloCorrectionProposal(f.Competition.Id,source.Id,f.Owner.Id,f.RightTeam.Id,0,1);
            var preview=(await store.PreviewAsync(proposal,ct))!;
            await Assert.That((await store.BeginCorrectionAsync(new(proposal with {ActorId=f.Left.Id},preview.PreviewId,"unauthorized"),ct)).Failure).IsEqualTo(LiveSoloFailure.Forbidden);
            var begun=(await store.BeginCorrectionAsync(new(proposal,preview.PreviewId,"investigation"),ct)).Correction!;
            f.Now=f.Now.AddSeconds(20);
            var canceled=await store.ResolveCorrectionAsync(new(f.Competition.Id,source.Id,f.Owner.Id,begun.Id,begun.ConcurrencyStamp,false,"original result verified",[]),ct);
            await Assert.That(canceled.Failure).IsNull();await Assert.That(source.WinnerTeamId).IsEqualTo(f.LeftTeam.Id);
            await Assert.That(downstream.State).IsEqualTo(LiveSoloMatchState.Running);await Assert.That(downstream.PendingCorrectionId).IsNull();
            var pause=await f.Db.Set<LiveSoloPauseInterval>().SingleAsync(x=>x.RoundId==downstream.CurrentRoundId&&x.Source==LiveSoloPauseSource.ResultCorrection,ct);
            await Assert.That(pause.EndedAt-pause.StartedAt).IsEqualTo(TimeSpan.FromSeconds(20));
            await Assert.That(await f.Db.LiveSoloMatches.CountAsync(ct)).IsEqualTo(3);
        });
    }
    [Test,Timeout(300_000)]
    public async Task Changed_preview_and_rollback_do_not_freeze_a_match_and_concurrent_begin_creates_one_request(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f=await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);var source=await FinishedSource(f,ct);var store=f.Store(f.Db);
            var proposal=new LiveSoloCorrectionProposal(f.Competition.Id,source.Id,f.Owner.Id,f.RightTeam.Id,0,1);
            var preview=(await store.PreviewAsync(proposal,ct))!;
            var final=await f.Db.LiveSoloMatches.SingleAsync(x=>x.Lane==LiveSoloBracketLane.GrandFinal,ct);
            final.ConcurrencyStamp=Guid.NewGuid();await f.Db.SaveChangesAsync(ct);
            await Assert.That((await store.BeginCorrectionAsync(new(proposal,preview.PreviewId,"stale"),ct)).Failure).IsEqualTo(LiveSoloFailure.Conflict);
            preview=(await store.PreviewAsync(proposal,ct))!;
            await using(var failing=new NoCTF.Infrastructure.Persistence.NoCtfDbContext(f.Options))
            {
                failing.SavingChanges+=(_,_)=>throw new InvalidOperationException("rollback");
                await Assert.That(async()=>await f.Store(failing).BeginCorrectionAsync(new(proposal,preview.PreviewId,"rollback"),ct)).Throws<InvalidOperationException>();
            }
            await Assert.That(await f.Db.Set<LiveSoloResultCorrection>().CountAsync(ct)).IsEqualTo(0);
            async Task<LiveSoloCorrectionResult> Begin(){await using var db=new NoCTF.Infrastructure.Persistence.NoCtfDbContext(f.Options);
                return await f.Store(db).BeginCorrectionAsync(new(proposal,preview.PreviewId,"concurrent"),ct);}
            var results=await Task.WhenAll(Begin(),Begin());
            await Assert.That(results.Count(x=>x.Correction is not null)).IsEqualTo(1);
            await Assert.That(await f.Db.Set<LiveSoloResultCorrection>().CountAsync(ct)).IsEqualTo(1);
        });
    }
    private static async Task<LiveSoloMatch> FinishedSource(LiveSoloMatchPersistenceTests.Fixture f,CancellationToken ct)
    {
        ((LiveSoloCompetitionModeConfiguration)f.Competition.ModeConfiguration!).BracketFormat=LiveSoloBracketFormat.DoubleElimination;await f.Db.SaveChangesAsync(ct);
        var generated=await f.Store(f.Db).GenerateAsync(new(f.Competition.Id,f.Owner.Id,f.Competition.ConcurrencyStamp,[f.LeftTeam.Id,f.RightTeam.Id],f.Now),ct);
        var id=generated.Bracket!.Matches.Single(x=>x.Match.State==LiveSoloMatchState.Preparing).Match.Id;
        await f.PrepareAsync(ct,id);await f.StartAsync(ct);var q=f.Round.Questions.Single(x=>x.Position==0);
        await f.Store(f.Db).AdmitAsync(new(f.Competition.Id,id,f.Round.Id,q.Id,f.Left.Id,"flag{first}",f.Now),ct);await f.Store(f.Db).ResolveAsync(f.Round.Id,f.Now,ct);
        return await f.Db.LiveSoloMatches.Include(x=>x.Slots).Include(x=>x.Roster).SingleAsync(x=>x.Id==id,ct);
    }
    [Test,Timeout(300_000)]
    public async Task Changing_only_the_tally_does_not_freeze_or_replace_a_started_downstream_match(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f=await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);var source=await FinishedSource(f,ct);
            var downstream=await StartedFinal(f,source,ct);source.LeftWins=0;await f.Db.SaveChangesAsync(ct);
            var store=f.Store(f.Db);var proposal=new LiveSoloCorrectionProposal(f.Competition.Id,source.Id,f.Owner.Id,f.LeftTeam.Id,1,0);
            var preview=(await store.PreviewAsync(proposal,ct))!;await Assert.That(preview.Downstream.Count).IsEqualTo(0);
            var begun=(await store.BeginCorrectionAsync(new(proposal,preview.PreviewId,"repair tally"),ct)).Correction!;
            await Assert.That(downstream.PendingCorrectionId).IsNull();await Assert.That(downstream.State).IsEqualTo(LiveSoloMatchState.Running);
            var result=await store.ResolveCorrectionAsync(new(f.Competition.Id,source.Id,f.Owner.Id,begun.Id,begun.ConcurrencyStamp,true,"verified tally",[]),ct);
            await Assert.That(result.Failure).IsNull();await Assert.That(source.LeftWins).IsEqualTo(1);
            await Assert.That(downstream.ReplacementMatchId).IsNull();await Assert.That(await f.Db.LiveSoloMatches.CountAsync(ct)).IsEqualTo(3);
            var history=(await store.ListCorrectionsAsync(f.Competition.Id,source.Id,f.Owner.Id,ct))!;
            await Assert.That(history.Single().PreviousLeftWins).IsEqualTo(0);await Assert.That(history.Single().ActorName).IsEqualTo(f.Owner.UserName);
            await Assert.That(await store.ListCorrectionsAsync(f.Competition.Id,source.Id,f.Left.Id,ct)).IsNull();
        });
    }
    private static async Task<LiveSoloMatch> StartedFinal(LiveSoloMatchPersistenceTests.Fixture f,LiveSoloMatch source,CancellationToken ct)
    {
        var match=await f.Db.LiveSoloMatches.Include(x=>x.Slots).Include(x=>x.Roster).SingleAsync(x=>x.Lane==LiveSoloBracketLane.GrandFinal,ct);
        var round=new LiveSoloRound {Id=Guid.NewGuid(),MatchId=match.Id,QuestionGroupId=f.Round.QuestionGroupId,Number=1,State=LiveSoloRoundState.Running,
            CreatedAt=f.Now,StartedAt=f.Now,LimitSeconds=30};
        round.Questions.Add(new() {Id=Guid.NewGuid(),RoundId=round.Id,CompetitionChallengeId=f.Entries[0].Id,OpenedAt=f.Now,Readiness=LiveSoloQuestionReadiness.Ready});
        f.Db.Add(round);match.CurrentRoundId=round.Id;match.StartedAt=f.Now;match.State=LiveSoloMatchState.Running;
        foreach(var slot in match.Slots){slot.RosterLockedAt=f.Now;slot.ReadyConfirmedAt=f.Now;}
        f.Db.Set<LiveSoloRosterMember>().AddRange(source.Roster.Select(x=>new LiveSoloRosterMember {MatchId=match.Id,TeamId=x.TeamId,UserId=x.UserId,ConfirmedAt=f.Now}));
        await f.Db.SaveChangesAsync(ct);return match;
    }
    [Test,Timeout(300_000)]
    public async Task Concurrent_apply_consumes_the_request_once_and_failed_application_rolls_back_the_replacement_graph(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f=await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);var source=await FinishedSource(f,ct);
            var store=f.Store(f.Db);var proposal=new LiveSoloCorrectionProposal(f.Competition.Id,source.Id,f.Owner.Id,f.RightTeam.Id,0,1);
            var preview=(await store.PreviewAsync(proposal,ct))!;var pending=(await store.BeginCorrectionAsync(new(proposal,preview.PreviewId,"review"),ct)).Correction!;
            var command=new ResolveLiveSoloCorrection(f.Competition.Id,source.Id,f.Owner.Id,pending.Id,pending.ConcurrencyStamp,true,"verified replacement",[]);
            await using(var failing=new NoCTF.Infrastructure.Persistence.NoCtfDbContext(f.Options))
            {
                failing.SavingChanges+=(_,_)=>throw new InvalidOperationException("rollback apply");
                await Assert.That(async()=>await f.Store(failing).ResolveCorrectionAsync(command,ct)).Throws<InvalidOperationException>();
            }
            await Assert.That(await f.Db.LiveSoloMatches.CountAsync(ct)).IsEqualTo(3);
            await Assert.That((await f.Db.LiveSoloMatches.AsNoTracking().SingleAsync(x=>x.Id==source.Id,ct)).WinnerTeamId).IsEqualTo(f.LeftTeam.Id);
            async Task<LiveSoloCorrectionResult> Apply(){await using var db=new NoCTF.Infrastructure.Persistence.NoCtfDbContext(f.Options);return await f.Store(db).ResolveCorrectionAsync(command,ct);}
            var results=await Task.WhenAll(Apply(),Apply());await Assert.That(results.Count(x=>x.Failure is null)).IsEqualTo(1);
            await Assert.That(results.Count(x=>x.Failure==LiveSoloFailure.Conflict)).IsEqualTo(1);
            await Assert.That(await f.Db.LiveSoloMatches.CountAsync(x=>x.SupersededAt==null,ct)).IsEqualTo(3);
            await Assert.That(await f.Db.LiveSoloMatches.CountAsync(x=>x.BracketGeneration==1,ct)).IsEqualTo(2);
            await Assert.That(await f.Db.LiveSoloActiveTeamSlots.CountAsync(ct)).IsEqualTo(2);
        });
    }
}
