using Microsoft.EntityFrameworkCore;
using NoCTF.Application.LiveSolo.Media;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Domain.LiveSolo;
using NoCTF.Domain.Identity;

namespace NoCTF.Infrastructure.LiveSolo.Media;

public sealed partial class LiveSoloCaptureStore
{
    public async Task<IReadOnlyList<LiveSoloProgramDecisionView>?> ProgramDecisionsAsync(Guid competitionId,Guid matchId,Guid actorId,CancellationToken ct)
    {
        if(!await db.Users.AnyAsync(x=>x.Id==actorId&&x.AccountStatus==UserAccountStatus.Active,ct)
            ||!await authorizer.CanObserveAsync(actorId,competitionId,ct))return null;
        return await db.Set<LiveSoloProgramDecision>().AsNoTracking().Where(x=>db.LiveSoloMediaSessions.Any(s=>s.Id==x.MediaSessionId&&s.MatchId==matchId
            &&db.LiveSoloMatches.Any(m=>m.Id==s.MatchId&&m.CompetitionId==competitionId))).OrderByDescending(x=>x.OccurredAt).Take(100)
            .Select(x=>new LiveSoloProgramDecisionView(x.Id,x.ProgramCaptureId,x.Action,x.Reason,x.OccurredAt,x.PreviousState,x.State)).ToArrayAsync(ct);
    }
    public async Task<LiveSoloProgramHealth?> HealthAsync(Guid competitionId,Guid matchId,Guid actorId,CancellationToken ct)
    {
        if(!await db.Users.AnyAsync(x=>x.Id==actorId&&x.AccountStatus==UserAccountStatus.Active,ct)
            ||!await authorizer.CanObserveAsync(actorId,competitionId,ct))return null;
        var program=await db.LiveSoloProgramCaptures.AsNoTracking().SingleOrDefaultAsync(x=>
            db.LiveSoloMediaSessions.Any(s=>s.Id==x.MediaSessionId&&s.CurrentProgramCaptureId==x.Id
                &&db.LiveSoloMatches.Any(m=>m.Id==s.MatchId&&m.Id==matchId&&m.CompetitionId==competitionId&&m.CurrentMediaSessionId==s.Id)),ct);
        return program is null?null:ProgramHealth(program);
    }
    public async Task<LiveSoloProgramRecoveryResult> RecoverAsync(RecoverLiveSoloProgram command,CancellationToken ct)
    {
        if(!await db.Users.AnyAsync(x=>x.Id==command.ActorId&&x.AccountStatus==UserAccountStatus.Active,ct)
            ||!await authorizer.CanModerateAsync(command.ActorId,command.CompetitionId,ct))return new(null,LiveSoloFailure.Forbidden);
        var snapshot=await db.LiveSoloProgramCaptures.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==command.ProgramId
            &&db.LiveSoloMediaSessions.Any(s=>s.Id==x.MediaSessionId&&s.MatchId==command.MatchId
                &&db.LiveSoloMatches.Any(m=>m.Id==s.MatchId&&m.CompetitionId==command.CompetitionId)),ct);
        if(snapshot is null)return new(null,LiveSoloFailure.NotFound);
        if(snapshot.ConcurrencyStamp!=command.ExpectedStamp)return new(null,LiveSoloFailure.Conflict);
        var session=await db.LiveSoloMediaSessions.AsNoTracking().SingleAsync(x=>x.Id==snapshot.MediaSessionId,ct);
        LiveSoloExportObservation? observed;
        try { observed=(await egress.ListAsync(session.RoomIdentity,ct)).SingleOrDefault(x=>x.RequestId==snapshot.Id&&x.RoomIdentity==session.RoomIdentity
            &&(snapshot.EgressId==null||x.Id==snapshot.EgressId)); }
        catch(Exception ex) when(ex is HttpRequestException or IOException or TimeoutException || ex is TaskCanceledException&&!ct.IsCancellationRequested)
        {return new(null,LiveSoloFailure.DependencyUnavailable);}
        var checkedAt=clock.GetUtcNow();LiveSoloFailure? failure=null;LiveSoloProgramHealth? result=null;
        await TransactionAsync(async()=>{
            messages.DiscardPendingMessages();failure=null;result=null;
            var program=await db.LiveSoloProgramCaptures.SingleAsync(x=>x.Id==snapshot.Id,ct);await db.Entry(program).ReloadAsync(ct);
            var currentSession=await db.LiveSoloMediaSessions.SingleAsync(x=>x.Id==session.Id,ct);await db.Entry(currentSession).ReloadAsync(ct);
            if(program.ConcurrencyStamp!=command.ExpectedStamp || currentSession.CurrentProgramCaptureId!=program.Id
                ||currentSession.State!=LiveSoloMediaState.Ready || !await db.LiveSoloMatches.AnyAsync(m=>m.Id==currentSession.MatchId
                    &&m.CurrentMediaSessionId==currentSession.Id&&m.State!=LiveSoloMatchState.Completed&&m.State!=LiveSoloMatchState.Canceled,ct))
            {failure=LiveSoloFailure.Conflict;return;}
            if(!await authorizer.CanModerateAsync(command.ActorId,command.CompetitionId,ct)
                ||!await db.Users.AnyAsync(x=>x.Id==command.ActorId&&x.AccountStatus==UserAccountStatus.Active,ct))
            {failure=LiveSoloFailure.Forbidden;return;}
            if(observed is null||!LiveSoloMediaPolicy.FreshStartProof(checkedAt,clock.GetUtcNow()))
            {failure=LiveSoloFailure.NotReady;return;}
            var decision=new LiveSoloProgramDecision {Id=Guid.CreateVersion7(clock.GetUtcNow()),ProgramCaptureId=program.Id,MediaSessionId=session.Id,
                ActorUserId=command.ActorId,Action=command.Action,Reason=command.Reason,OccurredAt=clock.GetUtcNow(),PreviousState=program.State};
            switch(command.Action)
            {
                case LiveSoloProgramAction.ReconcileExport:
                    if(program.State!=LiveSoloCaptureState.RequiresReview){failure=LiveSoloFailure.NotReady;return;}
                    program.EgressId=observed.Id;program.StartedAt??=observed.StartedAt;program.EndedAt=observed.EndedAt;program.State=ProgramState(observed.State);
                    break;
                case LiveSoloProgramAction.RetryImport:
                    if(observed.State!=LiveSoloExportState.Complete){failure=LiveSoloFailure.NotReady;return;}
                    program.EgressId=observed.Id;program.StartedAt??=observed.StartedAt;program.EndedAt=observed.EndedAt;program.State=LiveSoloCaptureState.Completed;
                    program.ImportedAt=null;
                    break;
                case LiveSoloProgramAction.Rotate:
                    if(observed.State is LiveSoloExportState.Active or LiveSoloExportState.Starting or LiveSoloExportState.Ending)
                    {program.RotationRequested=true;program.State=ProgramState(observed.State);}
                    else if(observed.State is LiveSoloExportState.Complete or LiveSoloExportState.Failed or LiveSoloExportState.Aborted or LiveSoloExportState.LimitReached)
                    {
                        if(observed.State==LiveSoloExportState.Complete && program.ImportedAt==null){failure=LiveSoloFailure.NotReady;return;}
                        program.State=ProgramState(observed.State);
                        if(observed.State!=LiveSoloExportState.Complete)program.RawCleanupAuthorizedAt=clock.GetUtcNow();
                        var next=new LiveSoloProgramCapture {Id=Guid.CreateVersion7(clock.GetUtcNow()),MediaSessionId=session.Id,CreatedAt=clock.GetUtcNow()};
                        db.LiveSoloProgramCaptures.Add(next);await db.SaveChangesAsync(ct);currentSession.CurrentProgramCaptureId=next.Id;
                        if(observed.State!=LiveSoloExportState.Complete)
                            await messages.PublishAsync(new RemoveLiveSoloCaptureFiles(program.Id));
                    }
                    else {failure=LiveSoloFailure.NotReady;return;}
                    break;
                default:failure=LiveSoloFailure.InvalidConfiguration;return;
            }
            program.ConcurrencyStamp=Guid.NewGuid();decision.State=program.State;db.Set<LiveSoloProgramDecision>().Add(decision);
            await messages.PublishAsync(new AdvanceLiveSoloCapture(session.Id));await db.SaveChangesAsync(ct);result=ProgramHealth(program);
        },ct);
        if(failure is null)await messages.FlushCommittedMessagesAsync();
        return new(result,failure);
    }
    private static LiveSoloProgramHealth ProgramHealth(LiveSoloProgramCapture program)=>new(program.Id,program.ConcurrencyStamp,program.State,
        program.StalledAt!=null,program.LastFragmentImportedAt,program.RotationRequested);
}
