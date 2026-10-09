using Microsoft.EntityFrameworkCore;
using NoCTF.Application.LiveSolo.Media;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.Infrastructure.LiveSolo.Media;

public sealed partial class LiveSoloCaptureStore
{
    public async Task<LiveSoloFailure?> RecoverRecordingAsync(ChangeLiveSoloRecording command,CancellationToken ct)
    {
        var snapshot=await db.LiveSoloRecordings.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==command.RecordingId
            && db.LiveSoloMediaSessions.Any(s=>s.Id==x.MediaSessionId&&s.MatchId==command.MatchId
                && db.LiveSoloMatches.Any(m=>m.Id==s.MatchId&&m.CompetitionId==command.CompetitionId)),ct);
        if(snapshot is null)return LiveSoloFailure.NotFound;
        if(snapshot.ConcurrencyStamp!=command.ExpectedStamp)return LiveSoloFailure.Conflict;
        LiveSoloExportObservation? observed=null;
        if(command.Action is LiveSoloRecordingAction.ReconcileExport or LiveSoloRecordingAction.RetryArchive or LiveSoloRecordingAction.StartNewChunk)
        {
            var room=await db.LiveSoloMediaSessions.Where(x=>x.Id==snapshot.MediaSessionId).Select(x=>x.RoomIdentity).SingleAsync(ct);
            try { observed=(await egress.ListAsync(room,ct)).SingleOrDefault(x=>x.RequestId==snapshot.Id&&x.RoomIdentity==room
                && (snapshot.EgressId==null || x.Id==snapshot.EgressId)); }
            catch(Exception ex) when(ex is HttpRequestException or IOException or TimeoutException || ex is TaskCanceledException&&!ct.IsCancellationRequested)
            {return LiveSoloFailure.DependencyUnavailable;}
        }
        var proofAt=clock.GetUtcNow();LiveSoloFailure? failure=null;
        await TransactionAsync(async () =>
        {
            messages.DiscardPendingMessages();
            failure=null;
            var record=await db.LiveSoloRecordings.SingleAsync(x=>x.Id==snapshot.Id,ct);
            await db.Entry(record).ReloadAsync(ct);
            if(record.ConcurrencyStamp!=command.ExpectedStamp || record.State==LiveSoloRecordingState.Deleting)
            {failure=LiveSoloFailure.Conflict;return;}
            var session=await db.LiveSoloMediaSessions.Include(x=>x.Participants).SingleAsync(x=>x.Id==record.MediaSessionId,ct);
            var manager=await db.Users.AnyAsync(x=>x.Id==command.ActorId&&x.AccountStatus==NoCTF.Domain.Identity.UserAccountStatus.Active,ct)
                && await authorizer.CanModerateAsync(command.ActorId,command.CompetitionId,ct);
            if(!manager){failure=LiveSoloFailure.Forbidden;return;}
            var decision=new LiveSoloRecordingDecision {Id=Guid.CreateVersion7(clock.GetUtcNow()),RecordingId=record.Id,MediaSessionId=record.MediaSessionId,
                ActorUserId=command.ActorId,Action=command.Action,Reason=command.Reason,OccurredAt=clock.GetUtcNow(),PreviousState=record.State,
                PreviousHold=record.DisputeHold,Hold=record.DisputeHold,PreviousPublished=record.Published,Published=record.Published};
            var active=session.State==LiveSoloMediaState.Ready && session.RecordingEnabled
                && await db.LiveSoloMatches.AnyAsync(x=>x.Id==session.MatchId&&x.CurrentMediaSessionId==session.Id&&x.SupersededAt==null
                    &&x.State!=LiveSoloMatchState.Completed&&x.State!=LiveSoloMatchState.Canceled,ct)
                && session.Participants.Any(x=>x.UserId==record.UserId&&x.ScreenTrackId==record.VideoTrackId&&x.ScreenState==LiveSoloScreenState.Sharing);
            switch(command.Action)
            {
                case LiveSoloRecordingAction.RetryPendingStart:
                    if(!active || record.State!=LiveSoloRecordingState.RequiresReview || record.RequestedAt!=null || record.EgressId!=null
                        || record.ReservedBytes!=0 || record.FileId!=null){failure=LiveSoloFailure.NotReady;return;}
                    if(checked(options.RecordingExportLimitBytes*2)>options.RecordingQuotaBytes-await RecordingCapacityUsedAsync(ct))
                    {failure=LiveSoloFailure.NotReady;return;}
                    record.State=LiveSoloRecordingState.Pending;record.Failure=null;
                    break;
                case LiveSoloRecordingAction.ReconcileExport:
                case LiveSoloRecordingAction.RetryArchive:
                    if(record.State!=LiveSoloRecordingState.RequiresReview || observed is null
                        || !LiveSoloMediaPolicy.FreshStartProof(proofAt,clock.GetUtcNow())
                        || command.Action==LiveSoloRecordingAction.RetryArchive&&observed.State!=LiveSoloExportState.Complete)
                    {failure=LiveSoloFailure.NotReady;return;}
                    if(command.Action==LiveSoloRecordingAction.RetryArchive)
                    {
                        var output=observed.Files.SingleOrDefault(x=>x.ObjectKey.EndsWith("/recording.mp4",StringComparison.Ordinal));
                        if(output is null || output.ByteLength<=0 || output.ByteLength>options.RecordingExportLimitBytes
                            || checked(output.ByteLength*2)>options.RecordingQuotaBytes-await RecordingCapacityUsedAsync(ct)+record.ReservedBytes)
                        {failure=LiveSoloFailure.NotReady;return;}
                    }
                    record.EgressId=observed.Id;record.StartedAt??=observed.StartedAt;record.EndedAt=observed.EndedAt;
                    record.State=RecordingState(observed.State);record.Failure=null;
                    if(observed.State is LiveSoloExportState.Failed or LiveSoloExportState.Aborted or LiveSoloExportState.LimitReached)
                        record.Failure=LiveSoloRecordingFailure.ExportFailed;
                    break;
                case LiveSoloRecordingAction.StartNewChunk:
                    if(!active || record.State!=LiveSoloRecordingState.Failed || record.RawRemovedAt==null || record.ReservedBytes!=0 || observed is null
                        || observed.State is not (LiveSoloExportState.Failed or LiveSoloExportState.Aborted or LiveSoloExportState.LimitReached)
                        || !LiveSoloMediaPolicy.FreshStartProof(proofAt,clock.GetUtcNow())
                        || await db.LiveSoloRecordings.AnyAsync(x=>x.MediaSessionId==record.MediaSessionId&&x.UserId==record.UserId
                            &&x.VideoTrackId==record.VideoTrackId&&x.Chunk>record.Chunk,ct)){failure=LiveSoloFailure.NotReady;return;}
                    if(checked(options.RecordingExportLimitBytes*2)>options.RecordingQuotaBytes-await RecordingCapacityUsedAsync(ct))
                    {failure=LiveSoloFailure.NotReady;return;}
                    var replacement=new LiveSoloRecording {Id=Guid.CreateVersion7(clock.GetUtcNow()),MediaSessionId=record.MediaSessionId,
                        UserId=record.UserId,VideoTrackId=record.VideoTrackId,Chunk=checked(record.Chunk+1),State=LiveSoloRecordingState.Pending,
                        CreatedAt=clock.GetUtcNow(),KeepUntil=clock.GetUtcNow().AddDays(session.RecordingRetentionDays)};
                    db.LiveSoloRecordings.Add(replacement);decision.ReplacementRecordingId=replacement.Id;
                    break;
                default:failure=LiveSoloFailure.InvalidConfiguration;return;
            }
            record.ConcurrencyStamp=Guid.NewGuid();decision.State=record.State;db.LiveSoloRecordingDecisions.Add(decision);
            await messages.PublishAsync(new AdvanceLiveSoloCapture(session.Id));
            if(record.State==LiveSoloRecordingState.Failed)await messages.PublishAsync(new RemoveLiveSoloRecordingRaw(record.Id));
        },ct);
        if(failure is null)await messages.FlushCommittedMessagesAsync();
        return failure;
    }
}
